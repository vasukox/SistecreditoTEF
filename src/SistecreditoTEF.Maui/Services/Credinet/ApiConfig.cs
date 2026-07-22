using Microsoft.Extensions.Configuration;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// Configuracion centralizada de CREDINET.
/// Equivalente 1:1 al ApiConfig.kt de Kotlin.
///
/// Aplica KISS: un solo lugar donde se cambian URLs, timeouts y headers.
/// Aplica DRY: cualquier capa que necesite hablar con CREDINET consume de aqui.
///
/// Las credenciales (API key, storeId, baseUrl) NO estan hardcodeadas:
/// vienen de IConfiguration (appsettings.json + User Secrets en dev).
/// </summary>
public record ApiConfig
{
    public required string SubscriptionKey { get; init; }
    public string? StoreId { get; init; }
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Canal de envío del OTP. Segun el manual Credinet (§4.2.3.3): 1 = WhatsApp,
    /// 0 (o no enviar) = SMS, y el manual RECOMIENDA WhatsApp. Por eso el
    /// appsettings usa 1. Configurable por "Credinet:OtpDestination" (o por
    /// CloudLicense) sin recompilar. El fallback de codigo (si no hay config)
    /// es 0 = SMS.
    /// </summary>
    public int OtpDestination { get; init; }

    /// <summary>HU8-973: constantes del manual, ahora configurables (no hardcoded).</summary>
    public string Source     { get; init; } = "2";
    public int    AuthMethod { get; init; } = 1;
    public int    Frequency  { get; init; } = 30;
    public int    TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// HU8-973: politica anti-spam de OTP.
    /// - OtpResendCooldownSeconds: segundos minimos entre dos getCreditToken
    ///   consecutivos. Default 60 (conservador).
    /// - OtpMaxResends: tope de reenvios por transaccion. Default 3.
    /// - OtpVerifyCooldownSeconds: segundos entre intentos fallidos de
    ///   verificacion para evitar rate-limit de Credinet. Default 2.
    /// Configurable via appsettings.json o CloudConfigStore sin recompilar.
    /// </summary>
    public int OtpResendCooldownSeconds { get; init; } = 60;
    public int OtpMaxResends            { get; init; } = 3;
    public int OtpVerifyCooldownSeconds { get; init; } = 2;

    /// <summary>Solo para logging/diagnóstico: "sandbox" | "production".</summary>
    public string Environment { get; init; } = "sandbox";

    /// <summary>
    /// HU8-973: pines SPKI (SHA-256, base64) para certificate pinning contra
    /// api.credinet.co. VACÍO por defecto = validación TLS estándar (no rompe
    /// nada). Cuando Sistecrédito entregue el/los pin(es), se cargan por
    /// "Credinet:CertificatePins" y el pinning se activa solo.
    /// </summary>
    public IReadOnlyList<string> CertificatePins { get; init; } = [];

    public const long ConnectTimeoutSeconds = 30L;
    public const long ReadTimeoutSeconds    = 60L;
    public const long WriteTimeoutSeconds   = 60L;

    public const string HeaderSubscriptionKey = "Ocp-Apim-Subscription-Key";
    public const string HeaderAccept          = "Accept";
    public const string MimeJson              = "application/json";

    /// <summary>
    /// Clave sandbox de CREDINET (publicada en el manual).
    /// Es publica para pruebas - en produccion real va por User Secrets.
    /// </summary>
    public const string SandboxSubscriptionKey = "88dec4b8617c4644a239a8af283dc742";

    /// <summary>
    /// Construye ApiConfig desde IConfiguration. Busca la seccion "Credinet".
    ///
    /// appsettings.json esperado:
    ///   {
    ///     "Credinet": {
    ///       "SubscriptionKey": "..." | "__SANDBOX__",
    ///       "StoreId": "...",            // opcional
    ///       "BaseUrl": "https://api.credinet.co/pos/"
    ///     }
    ///   }
    ///
    /// Si SubscriptionKey = "__SANDBOX__", usa [SandboxSubscriptionKey].
    /// Si falta la key, lanza excepcion clara.
    ///
    /// Para sobreescribir en desarrollo local:
    ///   dotnet user-secrets set "Credinet:SubscriptionKey" "<tu-key-real>"
    /// </summary>
    public static ApiConfig FromConfiguration(IConfiguration config)
    {
        var section = config.GetSection("Credinet");

        // HU8-973 (Opción A): los parámetros que ICG carga en CloudLicense y llegan
        // por el INITIALIZE tienen PRIORIDAD sobre appsettings.json. Si no llegaron
        // (sandbox/dev), se usan los de appsettings.
        static string? Cloud(string key) => CloudConfigStore.Get(key);

        var subscriptionKey = Cloud(CloudConfigStore.SubscriptionKey) ?? section["SubscriptionKey"];
        if (string.IsNullOrWhiteSpace(subscriptionKey))
            throw new InvalidOperationException(
                "Falta Credinet:SubscriptionKey (ni CloudLicense ni appsettings.json). "
              + "Opciones: parámetro Cloud SUBSCRIPTION_KEY, appsettings.json, o "
              + "dotnet user-secrets set \"Credinet:SubscriptionKey\" \"<key>\"");

        if (subscriptionKey == "__SANDBOX__")
            subscriptionKey = SandboxSubscriptionKey;

        // El HttpClient.BaseAddress necesita '/' final para combinar rutas relativas.
        var baseUrl = Cloud(CloudConfigStore.ApiBaseUrl) ?? section["BaseUrl"] ?? "https://api.credinet.co/pos/";
        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        var storeIdRaw = Cloud(CloudConfigStore.StoreId) ?? section["StoreId"];
        var otpRaw = Cloud(CloudConfigStore.OtpDestination) ?? section["OtpDestination"];
        var otpDestination = int.TryParse(otpRaw, out var d) ? d : 0;
        var environment = Cloud(CloudConfigStore.Environment) ?? section["Environment"];

        return new ApiConfig
        {
            SubscriptionKey = subscriptionKey,
            StoreId = string.IsNullOrWhiteSpace(storeIdRaw) ? null : storeIdRaw,
            BaseUrl = baseUrl,
            OtpDestination = otpDestination,
            Source = string.IsNullOrWhiteSpace(section["Source"]) ? "2" : section["Source"]!,
            AuthMethod = int.TryParse(section["AuthMethod"], out var am) ? am : 1,
            Frequency = int.TryParse(section["Frequency"], out var fr) ? fr : 30,
            TimeoutSeconds = int.TryParse(section["TimeoutSeconds"], out var ts) ? ts : 30,
            Environment = string.IsNullOrWhiteSpace(environment) ? "sandbox" : environment,
            OtpResendCooldownSeconds = int.TryParse(section["OtpResendCooldownSeconds"], out var orc) ? orc : 60,
            OtpMaxResends = int.TryParse(section["OtpMaxResends"], out var omr) ? omr : 3,
            OtpVerifyCooldownSeconds = int.TryParse(section["OtpVerifyCooldownSeconds"], out var ovc) ? ovc : 2,
            CertificatePins = section.GetSection("CertificatePins").GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList()
        };
    }
}
