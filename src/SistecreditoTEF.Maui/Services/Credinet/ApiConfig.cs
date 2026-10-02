using System.Globalization;
using Microsoft.Extensions.Configuration;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Tiendas;

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

    /// <summary>
    /// StoreId QUE SE LE MANDA A CREDINET.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// EN SANDBOX VA NULL, Y NO ES UN OLVIDO
    /// ─────────────────────────────────────────────────────────────────────────
    /// Las tiendas de [CatalogoDeTiendas] salen de la hoja STOREID-SISTECREDITO,
    /// que es de PRODUCCION (<c>SitC_WsUrl = .../posprod</c>). El ambiente de
    /// pruebas de Credinet no las conoce, y cualquier llamada que lleve uno de esos
    /// identificadores vuelve con <c>errorCode 225 · StoreNotFound</c>.
    ///
    /// Verificado contra la API, misma cedula, con y sin el parametro:
    ///
    ///   getSimulatedMonthLimit  sin storeId -> 200 {"months":2}
    ///   getSimulatedMonthLimit  con storeId -> 400 StoreNotFound
    ///   getactivecredits        con storeId -> 400 StoreNotFound
    ///   getCreditDetails        con storeId -> 400 StoreNotFound
    ///
    /// Eso dejaba el sandbox sin simular creditos y sin poder cobrar un abono, con
    /// el mensaje "esta tienda no esta registrada en Sistecredito".
    ///
    /// La tienda elegida NO se descarta: sigue en <see cref="Tienda"/> y en
    /// <see cref="StoreName"/>, asi que el comprobante y las pantallas la muestran
    /// igual. Lo unico que no viaja es el identificador, porque el ambiente de
    /// pruebas no tiene con que resolverlo.
    /// </summary>
    public string? StoreId { get; init; }

    /// <summary>
    /// De donde salio el <see cref="StoreId"/>, y que dijo cada fuente.
    ///
    /// Se conserva entero —y no solo el valor resuelto— porque ante un conflicto
    /// hay que poder mostrarle al instalador LAS DOS tiendas para que sepa cual
    /// corregir. Ver [ResolucionDeTienda].
    /// </summary>
    public ResolucionDeTienda Tienda { get; init; } =
        ResolucionDeTienda.Resolver(null, null);

    /// <summary>
    /// Si el APK se EMPAQUETO para produccion, segun su propio appsettings.
    ///
    /// Es distinto de [IsProduction], que es el ambiente ya resuelto. La
    /// diferencia es justo donde vivia el defecto: un paquete de produccion al
    /// que CloudLicense degradaba a sandbox seguia pareciendo sano porque todas
    /// las comprobaciones miraban el ambiente resuelto.
    ///
    /// Este dato no se puede cambiar desde afuera: viaja dentro del APK.
    /// </summary>
    public bool PaqueteDeProduccion { get; init; }

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
    /// Como se llama una caja que todavia no tiene tienda. NO es un nombre de
    /// tienda: es la ausencia de uno, dicha en voz alta.
    ///
    /// Existe como constante para que no vuelva a aparecer un literal generico
    /// ("Permoda") haciendo de relleno. Ver el bloque de [FromConfiguration].
    /// </summary>
    public const string SinTienda = "(sin tienda)";

    /// <summary>
    /// ¿Este texto nombra al ambiente de produccion?
    ///
    /// Se compara por PREFIJO ("prod") y no por igualdad. La version anterior
    /// exigia exactamente "production" o "prod", asi que "produccion",
    /// "PRODUCCIÓN" o "PRODUCTIVO" —escrituras todas razonables para quien
    /// provisiona en CloudLicense— se leian como "no es produccion", y eso
    /// apagaba el envio de la tienda sin que nadie se enterara.
    ///
    /// Una comparacion exacta sobre un valor que teclea otra empresa, en otro
    /// idioma, no es una validacion: es una trampa.
    /// </summary>
    public static bool EsProduccion(string? ambiente) =>
        ambiente?.Trim().StartsWith("prod", StringComparison.OrdinalIgnoreCase) == true;

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

        // ─────────────────────────────────────────────────────────────────────
        // SIN TIENDA ELEGIDA NO SE OPERA, EN LOS DOS AMBIENTES
        // ─────────────────────────────────────────────────────────────────────
        // El resto de este bloque solo aprieta en produccion, y tiene sentido: son
        // incoherencias entre ambientes. Esta no. La tienda se elige a mano al montar
        // el terminal, y ese paso tiene que ser igual de obligatorio en pruebas: si
        // en sandbox se pudiera vender sin elegirla, el paso se descubriria el dia
        // que la caja pasa a produccion, con la tienda ya instalada y vendiendo.
        //
        // El mensaje dice QUE HACER y donde. Antes decia "esta caja no esta
        // provisionada como tienda", que describe un estado y deja al instalador
        // igual que estaba: no habia nada que el pudiera hacer desde la caja.
        if (!Tienda.SePuedeOperar)
        {
            problems.Add(
                "SIN TIENDA: esta caja todavia no tiene tienda elegida, asi que los creditos " +
                "no se pueden registrar a nombre de nadie. Eligela en Configuracion - " +
                "Tienda de esta caja, buscandola por codigo o por nombre.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // LA TIENDA ESTA ELEGIDA PERO NO VA A VIAJAR
        // ─────────────────────────────────────────────────────────────────────
        // Esta comprobacion existe por un defecto concreto: en un APK de
        // produccion, un ENVIRONMENT de CloudLicense que no se reconociera como
        // produccion apagaba el envio del StoreId. La caja seguia mostrando su
        // tienda en pantalla —muestra la ELEGIDA— y vendia contra la credencial
        // real SIN decir a nombre de quien, asi que Sistecredito atribuia los
        // creditos a su valor por defecto. Nadie se enteraba hasta conciliar.
        //
        // Se mira [PaqueteDeProduccion] y no [IsProduction] a proposito: el
        // ambiente resuelto es justamente el que estaba mal. Lo que no se puede
        // falsear es con que ambiente se empaqueto el APK.
        //
        // Operar asi es peor que no operar: la venta sale bien, el cajero entrega
        // la mercancia y el credito queda a nombre de otro. Se frena.
        if (PaqueteDeProduccion && Tienda.SePuedeOperar && string.IsNullOrWhiteSpace(StoreId))
        {
            problems.Add(
                "LA TIENDA NO VIAJA: este APK es de produccion y la caja tiene tienda " +
                $"elegida ({Tienda.StoreId}), pero el ambiente quedo resuelto como " +
                $"'{Environment}' y por eso el StoreId NO se le envia a Sistecredito. " +
                "Los creditos quedarian a nombre de otra tienda. Revisa el parametro " +
                "ENVIRONMENT de este terminal en CloudLicense.");
        }

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

            // La falta de tienda ya se reporta arriba, para los dos ambientes. Aca no
            // se repite: dos quejas por lo mismo hacen dudar de si son dos problemas.
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
    /// <param name="storeIdDeLaCaja">
    /// Tienda elegida por el instalador al montar el terminal (ver
    /// [ITiendaDeLaCaja]). Null = no se eligió ninguna.
    ///
    /// NO participa de la precedencia normal: se CRUZA con el de CloudLicense en
    /// [ResolucionDeTienda], porque si los dos existen y discrepan lo correcto no
    /// es preferir uno, es frenar.
    /// </param>
    public static ApiConfig FromConfiguration(
        IConfiguration config,
        ICloudConfig? cloud = null,
        string? storeIdDeLaCaja = null)
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

        // ─────────────────────────────────────────────────────────────────────
        // LA TIENDA NO SIGUE LA PRECEDENCIA DE LOS DEMAS PARAMETROS
        // ─────────────────────────────────────────────────────────────────────
        // Para todo lo demas vale "el primero que exista gana". Para el StoreId no:
        // si HioPosCloud dice una tienda y la caja fue configurada como otra,
        // preferir cualquiera de las dos es elegir al azar cual de los dos errores
        // se comete. [ResolucionDeTienda] las cruza y, ante discrepancia, deja el
        // StoreId en null para que [Validate] frene la operacion.
        //
        // Ni el appsettings ni CloudLicense la definen: los dos son valores que
        // llegan de afuera y que nadie reviso para ESTA caja. El del APK es el mismo
        // para las 76 tiendas que lo instalen —fue lo que puso los creditos de Suba
        // a nombre de la 037— y el de CloudLicense depende de que ICG provisione
        // terminal por terminal. El STORE_ID del POS se pasa igual, pero solo para
        // dejar constancia si no coincide.
        var tienda = ResolucionDeTienda.Resolver(
            Blank(cloud.Get(ICloudConfig.StoreId)),
            Blank(storeIdDeLaCaja));

        // ─────────────────────────────────────────────────────────────────────
        // EL NOMBRE DE LA TIENDA SALE DEL CATALOGO CUANDO ICG NO LO MANDA
        // ─────────────────────────────────────────────────────────────────────
        // Antes esto era `Value(STORE_NAME, "StoreName") ?? "Permoda"`, y como el
        // appsettings trae "Permoda", el generico ganaba SIEMPRE que CloudLicense
        // no mandara STORE_NAME. O sea que el comprobante de abono de las 76
        // tiendas decia lo mismo.
        //
        // ─────────────────────────────────────────────────────────────────────
        // NO HAY NOMBRE DE TIENDA POR DEFECTO
        // ─────────────────────────────────────────────────────────────────────
        // Esto terminaba en `?? "Permoda"`, y el appsettings trae "Permoda", asi
        // que una caja SIN tienda elegida mostraba "Permoda" en la cabecera, en el
        // comprobante y en el saludo entre cajas. O sea que el estado mas
        // peligroso del modulo —no saber a nombre de quien se vende— se veia
        // exactamente igual que el estado sano. Un valor por defecto aqui no es
        // una comodidad: es esconder el unico dato que hay que mirar.
        //
        // Y el orden cambio: ahora manda el CATALOGO sobre lo que diga ICG. El
        // nombre tiene que describir el StoreId que de verdad se esta mandando; si
        // CloudLicense dice "037" mientras la caja manda el id de la 012, mostrar
        // el nombre de ICG convierte la pantalla en una mentira coherente. El de
        // ICG queda de respaldo para una tienda que todavia no esta en la hoja.
        var nombreDeLaTienda =
            !tienda.SePuedeOperar
                ? SinTienda
                : CatalogoDeTiendas.PorStoreId(tienda.StoreId)?.NombreVisible
                  ?? Blank(cloud.Get(ICloudConfig.StoreName))
                  ?? TextoDeTienda.StoreId(tienda.StoreId);

        // ─────────────────────────────────────────────────────────────────────
        // EL AMBIENTE LO DECIDE EL PAQUETE. CLOUDLICENSE NO PUEDE DEGRADARLO.
        // ─────────────────────────────────────────────────────────────────────
        // Esto era `Value(ENVIRONMENT, "Environment")`, o sea que CloudLicense
        // GANABA. Y el ambiente decide si el StoreId viaja:
        //
        //     StoreId = esProduccion ? tienda.StoreId : null
        //
        // Junta las dos cosas y sale el defecto: un APK de PRODUCCION al que ICG
        // le mandara un ENVIRONMENT que no fuera exactamente "production" o
        // "prod" —"produccion" en español, "PRODUCTIVO", un valor de pruebas que
        // quedo de una homologacion— dejaba de mandar la tienda. En silencio:
        //
        //   · la pantalla seguia mostrando la tienda correcta, porque muestra la
        //     ELEGIDA ([Tienda]) y no la que viaja ([StoreId]);
        //   · [Validate] no se quejaba, porque todas sus comprobaciones de
        //     produccion estan dentro de `if (IsProduction)`, que era false;
        //   · y las peticiones salian a /posprod/ con la credencial real, pero sin
        //     tienda, asi que Sistecredito las atribuia a su valor por defecto.
        //
        // CloudLicense se provisiona TERMINAL POR TERMINAL. Por eso esto explica
        // que una tienda facture bien y la de al lado no, con el mismo APK y con
        // las dos bien configuradas en pantalla.
        //
        // La regla ahora: si el paquete es de produccion, el modulo es de
        // produccion y la tienda viaja. ICG puede PROMOVER un paquete de pruebas
        // (sigue sirviendo para homologar sin recompilar), nunca degradar uno de
        // produccion.
        var ambienteDelPaquete = Blank(section["Environment"]) ?? "sandbox";
        var ambienteDeIcg      = Blank(cloud.Get(ICloudConfig.Environment));

        var paqueteDeProduccion = EsProduccion(ambienteDelPaquete);
        var esProduccion        = paqueteDeProduccion || EsProduccion(ambienteDeIcg);

        var environment = esProduccion ? "production" : (ambienteDeIcg ?? ambienteDelPaquete);

        return new ApiConfig
        {
            SubscriptionKey = subscriptionKey,
            StoreId         = esProduccion ? tienda.StoreId : null,
            Tienda          = tienda,
            PaqueteDeProduccion = paqueteDeProduccion,
            BaseUrl         = baseUrl,
            OtpDestination  = Int(ICloudConfig.OtpDestination, "OtpDestination", 0),
            Environment     = environment,
            StoreName       = nombreDeLaTienda,
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
