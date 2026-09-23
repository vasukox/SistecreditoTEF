using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Credinet;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Persistencia de los parámetros de CloudLicense que llegan en el INITIALIZE.
///
/// El parseo del XML vive en [CloudConfigParser] (puro y testeado). Esta clase
/// solo se encarga del almacenamiento, que sí es específico de plataforma.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QA A-3 — DÓNDE SE GUARDA CADA COSA
/// ─────────────────────────────────────────────────────────────────────────────
/// Antes TODO iba a <c>Preferences</c> (SharedPreferences), que es privado de la
/// app pero **texto plano en disco**, incluida la <c>SUBSCRIPTION_KEY</c> de
/// producción de Azure APIM. Ahora:
///
///   • <c>SUBSCRIPTION_KEY</c> → [SecureStorage] (respaldado por Android
///     Keystore), igual que la llave de SQLCipher.
///   • El resto (URL, storeId, ambiente, destino OTP…) → <c>Preferences</c>.
///     No son secretos.
///
/// SecureStorage es asíncrono y el INITIALIZE es sincrónico, así que la key se
/// cachea en memoria de inmediato y se persiste en segundo plano. La primera
/// llamada HTTP ocurre mucho después (cuando el cajero ingresa la cédula), así
/// que la caché en memoria siempre está lista a tiempo; SecureStorage solo hace
/// falta para que sobreviva al reinicio del proceso.
/// </summary>
public static class CloudConfigStore
{
    private const string Prefix = "cloudparam_";
    private const string SecureKeyName = "cloud_subscription_key_v1";

    // Nombres de parámetros: se mantienen como alias de [ICloudConfig] para no
    // romper el código existente que los referenciaba desde aquí.
    public const string ApiBaseUrl      = ICloudConfig.ApiBaseUrl;
    public const string SubscriptionKey = ICloudConfig.SubscriptionKey;
    public const string StoreId         = ICloudConfig.StoreId;
    public const string Environment     = ICloudConfig.Environment;
    public const string OtpDestination  = ICloudConfig.OtpDestination;

    private static readonly object _gate = new();
    private static string? _cachedSubscriptionKey;
    private static bool _secureLoadAttempted;

    /// <summary>
    /// Deja la llave en cache ANTES de que alguien la pida por el camino sincrónico.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE
    /// ─────────────────────────────────────────────────────────────────────────
    /// <see cref="GetSubscriptionKey"/> es sincrónico —lo llama el armado de la
    /// petición HTTP, que no es async— y resuelve la lectura de SecureStorage con
    /// <c>GetAwaiter().GetResult()</c>. Está acotado a 3 segundos, pero sigue siendo
    /// un hilo bloqueado esperando al Keystore en una app de pagos.
    ///
    /// Precalentando al arrancar, ese camino encuentra la caché llena y no bloquea
    /// nada. El <c>GetResult()</c> se deja como respaldo: si el precalentamiento no
    /// llegó a correr —o falló— la llave se sigue consiguiendo, que es lo que no
    /// puede dejar de pasar.
    ///
    /// Es mejor esfuerzo y no devuelve nada: quien lo llama no tiene que decidir
    /// nada con el resultado.
    /// </summary>
    public static async Task PrecalentarAsync()
    {
        lock (_gate)
        {
            if (_cachedSubscriptionKey is not null || _secureLoadAttempted) return;
        }

        try
        {
            var stored = await SecureStorage.Default.GetAsync(SecureKeyName)
                .WaitAsync(TimeSpan.FromSeconds(5));

            lock (_gate)
            {
                // Si entremedio llegó un INITIALIZE con una llave nueva, esa manda:
                // lo que se acaba de leer del almacenamiento es más viejo.
                if (_cachedSubscriptionKey is null)
                    _cachedSubscriptionKey = string.IsNullOrWhiteSpace(stored) ? null : stored;

                _secureLoadAttempted = true;
            }

            AppLogger.I("CloudConfigStore",
                _cachedSubscriptionKey is null
                    ? "Precalentado: no habia llave guardada todavia."
                    : "Precalentado: la llave quedo en cache sin bloquear ningun hilo.");
        }
        catch (Exception ex)
        {
            // No se marca el intento como hecho: que falle el precalentamiento no
            // puede impedir que el camino sincrónico lo intente después.
            AppLogger.W("CloudConfigStore",
                $"No se pudo precalentar la llave: {ex.Message}. Se leera al primer uso.");
        }
    }

    /// <summary>
    /// Parsea el XML de Parameters y lo persiste. Devuelve la cantidad de
    /// parámetros reconocidos.
    ///
    /// QA C-3: devolver el conteo permite que [MainActivity] decida qué hacer
    /// cuando llegan CERO parámetros, en vez de continuar en silencio.
    /// </summary>
    public static int SaveFromXml(string? xml)
    {
        var values = CloudConfigParser.Parse(xml);

        if (values.Count == 0)
        {
            // QA C-3: condición anómala. Si HioPos mandó el extra Parameters, algo
            // tenía que traer. Se registra como ERROR, no como detalle.
            if (!string.IsNullOrWhiteSpace(xml))
            {
                AppLogger.E("CloudConfigStore",
                    "El XML de Parameters llego pero NO se reconocio ningun <Param>. " +
                    "Revisar el formato entregado por ICG: la app quedara con la " +
                    "configuracion de appsettings.json (sandbox).");

                // Diagnóstico de FORMA, sin valores: permite adaptar el parseo al
                // formato real que entrega ICG sin exponer la credencial de APIM.
                // Ver [CloudConfigParser.DescribeStructure].
                AppLogger.E("CloudConfigStore",
                    "ESTRUCTURA-PARAMETERS " + CloudConfigParser.DescribeStructure(xml));
            }
            return 0;
        }

        foreach (var (key, value) in values)
        {
            if (string.Equals(key, ICloudConfig.SubscriptionKey, StringComparison.OrdinalIgnoreCase))
                SetSubscriptionKey(value);
            else
                Preferences.Set(Prefix + key, value);
        }

        // No se loguean los valores: uno de ellos es la credencial de APIM.
        AppLogger.I("CloudConfigStore",
            $"Parametros Cloud guardados: {values.Count} [{string.Join(", ", values.Keys)}]");
        return values.Count;
    }

    /// <summary>Valor persistido de un parámetro Cloud, o null si no existe.</summary>
    public static string? Get(string key)
    {
        if (string.Equals(key, ICloudConfig.SubscriptionKey, StringComparison.OrdinalIgnoreCase))
            return GetSubscriptionKey();

        var v = Preferences.Get(Prefix + key, string.Empty);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    // ------------------------------------------------------------------
    // SUBSCRIPTION_KEY en SecureStorage (QA A-3)
    // ------------------------------------------------------------------

    private static void SetSubscriptionKey(string value)
    {
        lock (_gate)
        {
            _cachedSubscriptionKey = string.IsNullOrWhiteSpace(value) ? null : value;
            _secureLoadAttempted = true;
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        // Persistencia best-effort en segundo plano: si SecureStorage falla o se
        // cuelga, la caché en memoria ya cubre esta sesión del POS.
        //
        // Va por [Fire.AndForget] y no por un Task.Run con try/catch propio: la
        // protección contra que una tarea suelta tumbe el proceso no puede depender
        // de que cada autor se acuerde de escribirla.
        Fire.AndForget(async () =>
        {
            await SecureStorage.Default.SetAsync(SecureKeyName, value)
                .WaitAsync(TimeSpan.FromSeconds(5));
            AppLogger.I("CloudConfigStore", "SUBSCRIPTION_KEY persistida en SecureStorage.");
        }, "CloudConfigStore");
    }

    private static string? GetSubscriptionKey()
    {
        lock (_gate)
        {
            if (_cachedSubscriptionKey is not null) return _cachedSubscriptionKey;
            if (_secureLoadAttempted) return null;
            _secureLoadAttempted = true;
        }

        // Lectura sincrónica acotada: solo ocurre una vez por proceso y antes de
        // la primera petición HTTP, no en el hilo de arranque de MAUI.
        try
        {
            var stored = SecureStorage.Default.GetAsync(SecureKeyName)
                .WaitAsync(TimeSpan.FromSeconds(3))
                .GetAwaiter().GetResult();

            lock (_gate)
            {
                _cachedSubscriptionKey = string.IsNullOrWhiteSpace(stored) ? null : stored;
                return _cachedSubscriptionKey;
            }
        }
        catch (Exception ex)
        {
            AppLogger.W("CloudConfigStore",
                $"No se pudo leer la SUBSCRIPTION_KEY de SecureStorage: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Borra los parámetros persistidos. Se usa en el FINALIZE para no dejar la
    /// credencial viva más allá de la sesión de HioPosCloud.
    /// </summary>
    public static void Clear()
    {
        foreach (var key in new[]
                 {
                     ICloudConfig.ApiBaseUrl, ICloudConfig.StoreId, ICloudConfig.Environment,
                     ICloudConfig.OtpDestination, ICloudConfig.CertificatePins,
                     ICloudConfig.Frequency, ICloudConfig.Source, ICloudConfig.AuthMethod,
                     ICloudConfig.TimeoutSeconds, ICloudConfig.OtpMaxResends, ICloudConfig.StoreName
                 })
        {
            Preferences.Remove(Prefix + key);
        }

        lock (_gate)
        {
            _cachedSubscriptionKey = null;
            _secureLoadAttempted = false;
        }

        try { SecureStorage.Default.Remove(SecureKeyName); }
        catch (Exception ex)
        {
            AppLogger.W("CloudConfigStore", $"No se pudo limpiar SecureStorage: {ex.Message}");
        }
    }
}

/// <summary>
/// Adaptador de [CloudConfigStore] a [ICloudConfig] para inyectarlo en
/// [ApiConfig] y poder sustituirlo en tests.
/// </summary>
public sealed class PreferencesCloudConfig : ICloudConfig
{
    public string? Get(string key) => CloudConfigStore.Get(key);
}
