using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// Configuracion centralizada de CREDINET.
///
/// Aplica KISS: un solo lugar donde se cambian URLs, timeouts y headers.
/// Aplica DRY: cualquier capa que necesite hablar con CREDINET consume de aqui.
///
/// Precedencia (gana el primero que exista):
///   1. CloudLicense (ICG, llega en el INITIALIZE)
///   2. appsettings.json (embebido en el APK)
///   3. User Secrets (solo DEBUG)
///   4. Fallback en codigo
///
/// QA M-3: antes SOLO <c>SubscriptionKey</c>, <c>BaseUrl</c>, <c>StoreId</c>,
/// <c>OtpDestination</c> y <c>Environment</c> se leian de CloudLicense. El resto
/// (<c>Source</c>, <c>AuthMethod</c>, <c>Frequency</c>, <c>TimeoutSeconds</c>,
/// las politicas de OTP y los pines TLS) se leian solo de appsettings, pese a
/// que el comentario afirmaba que eran "configurables sin recompilar". Ahora
/// TODOS pasan por CloudLicense primero.
/// </summary>
public record ApiConfig
{
    public required string SubscriptionKey { get; init; }
    public string? StoreId { get; init; }
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Canal de envío del OTP. Manual Credinet §4.2.3.3: 1 = WhatsApp,
    /// 0 (o no enviar) = SMS. Sistecrédito confirmó WhatsApp para test y
    /// producción, por eso el appsettings usa 1.
    /// </summary>
    public int OtpDestination { get; init; }

    public string Source     { get; init; } = "2";
    public int    AuthMethod { get; init; } = 1;
    public int    Frequency  { get; init; } = 30;
    public int    TimeoutSeconds { get; init; } = 30;

    /// <summary>Política anti-spam de OTP (ver [OtpRequestThrottle]).</summary>
    public int OtpResendCooldownSeconds { get; init; } = 60;
    public int OtpMaxResends            { get; init; } = 3;
    public int OtpVerifyCooldownSeconds { get; init; } = 2;

    /// <summary>Tope de intentos de verificación del OTP por transacción (QA A-12).</summary>
    public int OtpMaxVerifyAttempts { get; init; } = 3;

    /// <summary>"sandbox" | "production". Determina la validación estricta.</summary>
    public string Environment { get; init; } = "sandbox";

    /// <summary>
    /// Nombre de la tienda para el voucher. QA M-7: antes estaba hardcodeado
    /// como "Permoda" en los ViewModels, así que todos los comprobantes de todas
    /// las tiendas KOAJ salían iguales.
    /// </summary>
    public string StoreName { get; init; } = "Permoda";

    /// <summary>
    /// Id del medio de pago para un RECAUDO (entrada de caja). Ver
    /// <see cref="ICloudConfig.PaymentMeanIdRecaudo"/>: un abono entra en
    /// efectivo, no en tarjeta.
    /// </summary>
    public string PaymentMeanIdRecaudo { get; init; } = "1";

    /// <summary>
    /// Id del medio de pago sobre el que se consolida Sistecredito en una VENTA.
    /// </summary>
    public string PaymentMeanIdVenta { get; init; } = "2";

    /// <summary>
    /// Habilita la impresión nativa Sunmi. Por defecto false: requiere el AIDL
    /// oficial del fabricante y validación en hardware (ver [SunmiPrinter]).
    /// </summary>
    public bool EnableSunmiNative { get; init; }

    /// <summary>
    /// Pines SPKI (SHA-256, base64) para certificate pinning contra Credinet.
    /// Vacío = validación TLS estándar.
    /// </summary>
    public IReadOnlyList<string> CertificatePins { get; init; } = [];

    public const long ConnectTimeoutSeconds = 30L;
    public const long ReadTimeoutSeconds    = 60L;
    public const long WriteTimeoutSeconds   = 60L;

    public const string HeaderSubscriptionKey = "Ocp-Apim-Subscription-Key";
    public const string HeaderAccept          = "Accept";
    public const string MimeJson              = "application/json";

    /// <summary>Marcador que en appsettings pide usar la key pública de sandbox.</summary>
    public const string SandboxKeyPlaceholder = "__SANDBOX__";

    /// <summary>
    /// Clave sandbox de CREDINET (publicada en el manual). Es pública y solo
    /// sirve para pruebas; en producción llega por CloudLicense.
    /// </summary>
    public const string SandboxSubscriptionKey = "88dec4b8617c4644a239a8af283dc742";

    /// <summary>True si la configuración dice que este POS opera en producción.</summary>
    public bool IsProduction =>
        Environment.Equals("production", StringComparison.OrdinalIgnoreCase)
        || Environment.Equals("prod", StringComparison.OrdinalIgnoreCase);

    /// <summary>True si se está usando la key pública de pruebas.</summary>
    public bool UsesSandboxKey =>
        string.Equals(SubscriptionKey, SandboxSubscriptionKey, StringComparison.Ordinal);

    /// <summary>
    /// QA C-3: valida coherencia entre ambiente y credenciales. Devuelve la lista
    /// de problemas (vacía = configuración sana).
    ///
    /// Es la barrera que impide el peor modo de fallo del módulo: una terminal de
    /// producción operando contra sandbox sin que nadie se entere. Antes esto no
    /// se comprobaba en ningún punto.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(SubscriptionKey))
            problems.Add("Falta la SUBSCRIPTION_KEY.");

        if (string.IsNullOrWhiteSpace(BaseUrl))
            problems.Add("Falta la API_BASE_URL.");
        else if (!BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            problems.Add($"La API_BASE_URL no usa HTTPS: '{BaseUrl}'.");

        if (IsProduction)
        {
            if (UsesSandboxKey)
                problems.Add(
                    "ENVIRONMENT=production pero se esta usando la SUBSCRIPTION_KEY publica de " +
                    "sandbox. ICG debe provisionar la key real en CloudLicense.");

            // La URL de sandbox es /pos/; la de produccion /posprod/.
            if (BaseUrl.Contains("/pos/", StringComparison.OrdinalIgnoreCase))
                problems.Add(
                    $"ENVIRONMENT=production pero la BaseUrl apunta a sandbox ('{BaseUrl}'). " +
                    "Se espera /posprod/.");

            if (string.IsNullOrWhiteSpace(StoreId))
                problems.Add("ENVIRONMENT=production sin STORE_ID configurado.");
        }

        return problems;
    }

    /// <summary>
    /// Construye ApiConfig aplicando la precedencia CloudLicense → appsettings →
    /// secrets → default.
    /// </summary>
    /// <param name="config">Configuración de .NET (appsettings + user secrets).</param>
    /// <param name="cloud">
    /// Parámetros de CloudLicense. Si es null se usa [EmptyCloudConfig], lo que
    /// equivale a "no llegó nada de ICG".
    /// </param>
    public static ApiConfig FromConfiguration(IConfiguration config, ICloudConfig? cloud = null)
    {
        var section = config.GetSection("Credinet");
        var printing = config.GetSection("Printing");
        cloud ??= EmptyCloudConfig.Instance;

        string? Value(string cloudKey, string settingsKey) =>
            Blank(cloud.Get(cloudKey)) ?? Blank(section[settingsKey]);

        int Int(string cloudKey, string settingsKey, int fallback) =>
            int.TryParse(Value(cloudKey, settingsKey), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var v) ? v : fallback;

        var subscriptionKey = Value(ICloudConfig.SubscriptionKey, "SubscriptionKey");
        if (string.IsNullOrWhiteSpace(subscriptionKey))
            throw new InvalidOperationException(
                "Falta Credinet:SubscriptionKey (ni CloudLicense ni appsettings.json). "
              + "Opciones: parametro Cloud SUBSCRIPTION_KEY, appsettings.json, o "
              + "dotnet user-secrets set \"Credinet:SubscriptionKey\" \"<key>\"");

        if (subscriptionKey == SandboxKeyPlaceholder)
            subscriptionKey = SandboxSubscriptionKey;

        // El HttpClient.BaseAddress necesita '/' final para combinar rutas relativas.
        var baseUrl = Value(ICloudConfig.ApiBaseUrl, "BaseUrl") ?? "https://api.credinet.co/pos/";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";

        return new ApiConfig
        {
            SubscriptionKey = subscriptionKey,
            StoreId         = Value(ICloudConfig.StoreId, "StoreId"),
            BaseUrl         = baseUrl,
            OtpDestination  = Int(ICloudConfig.OtpDestination, "OtpDestination", 0),
            Environment     = Value(ICloudConfig.Environment, "Environment") ?? "sandbox",
            StoreName       = Value(ICloudConfig.StoreName, "StoreName") ?? "Permoda",
            PaymentMeanIdRecaudo =
                Value(ICloudConfig.PaymentMeanIdRecaudo, "PaymentMeanIdRecaudo") ?? "1",
            PaymentMeanIdVenta =
                Value(ICloudConfig.PaymentMeanIdVenta, "PaymentMeanIdVenta") ?? "2",
            Source          = Value(ICloudConfig.Source, "Source") ?? "2",
            AuthMethod      = Int(ICloudConfig.AuthMethod, "AuthMethod", 1),
            Frequency       = Int(ICloudConfig.Frequency, "Frequency", 30),
            TimeoutSeconds  = Int(ICloudConfig.TimeoutSeconds, "TimeoutSeconds", 30),
            OtpResendCooldownSeconds = Int("OTP_RESEND_COOLDOWN", "OtpResendCooldownSeconds", 60),
            OtpMaxResends            = Int(ICloudConfig.OtpMaxResends, "OtpMaxResends", 3),
            OtpVerifyCooldownSeconds = Int("OTP_VERIFY_COOLDOWN", "OtpVerifyCooldownSeconds", 2),
            OtpMaxVerifyAttempts     = Int("OTP_MAX_VERIFY_ATTEMPTS", "OtpMaxVerifyAttempts", 3),
            EnableSunmiNative        = bool.TryParse(
                Blank(cloud.Get("ENABLE_SUNMI_NATIVE")) ?? Blank(printing["EnableSunmiNative"]),
                out var sunmi) && sunmi,
            CertificatePins = ReadPins(cloud, section)
        };
    }

    /// <summary>
    /// Pines TLS. QA A-5: ahora se aceptan también por CloudLicense, en una sola
    /// cadena separada por comas o punto y coma, porque CloudLicense entrega
    /// valores planos (no arreglos). Antes solo se leían de appsettings, así que
    /// el pinning no se podía activar sin recompilar el APK.
    /// </summary>
    private static IReadOnlyList<string> ReadPins(ICloudConfig cloud, IConfigurationSection section)
    {
        var fromCloud = Blank(cloud.Get(ICloudConfig.CertificatePins));
        if (fromCloud is not null)
        {
            return fromCloud
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        return section.GetSection("CertificatePins").GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToList();
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
