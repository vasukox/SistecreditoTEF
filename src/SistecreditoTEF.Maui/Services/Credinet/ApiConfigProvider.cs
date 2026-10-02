using Microsoft.Extensions.Configuration;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// QA M-2: mantiene el [ApiConfig] vigente y permite reconstruirlo cuando llegan
/// parámetros nuevos de CloudLicense.
///
/// EL PROBLEMA QUE RESUELVE: <c>ApiConfig</c> se registraba como singleton, así
/// que se construía UNA vez —la primera que alguien lo resolvía— y vivía todo el
/// proceso. La precedencia documentada ("CloudLicense gana sobre appsettings")
/// solo se cumplía si esa primera construcción ocurría **después** del
/// INITIALIZE, lo cual era accidental, no garantizado. Y si ICG cambiaba un
/// parámetro y HioPos reenviaba INITIALIZE sin que el proceso muriera, la app
/// seguía con la configuración vieja indefinidamente.
///
/// Ahora [MainActivity] llama <see cref="Reload"/> al procesar el INITIALIZE,
/// justo después de persistir los parámetros, y todo lo que lea a través del
/// proveedor ve los valores nuevos.
/// </summary>
public sealed class ApiConfigProvider : IApiConfigSource
{
    private readonly IConfiguration _configuration;
    private readonly ICloudConfig _cloud;
    private readonly Tiendas.ITiendaDeLaCaja? _tiendaDeLaCaja;
    private readonly object _gate = new();
    private ApiConfig? _current;

    /// <summary>
    /// <paramref name="tiendaDeLaCaja"/> es opcional para no romper a quien
    /// construya el proveedor sin ella (tests). En la app va siempre registrada:
    /// es la mitad local de la resolucion de tienda, y sin ella una caja que
    /// HioPosCloud no provisiono no tendria forma de operar.
    /// </summary>
    public ApiConfigProvider(
        IConfiguration configuration,
        ICloudConfig cloud,
        Tiendas.ITiendaDeLaCaja? tiendaDeLaCaja = null)
    {
        _configuration = configuration;
        _cloud = cloud;
        _tiendaDeLaCaja = tiendaDeLaCaja;
    }

    /// <summary>Configuración vigente. Se construye al primer acceso.</summary>
    public ApiConfig Current
    {
        get
        {
            lock (_gate)
            {
                if (_current is not null) return _current;

                _current = Build();

                // Tambien aca, y no solo en [Reload]: si HioPos nunca manda el
                // INITIALIZE, Reload no corre y sin esto no quedaria ninguna linea
                // diciendo a nombre de que tienda opera la caja. Ese es justamente
                // el caso que hay que poder ver.
                LogValidation(_current);
                return _current;
            }
        }
    }

    /// <summary>
    /// Reconstruye la configuración desde CloudLicense + appsettings y registra
    /// en el log si cambió algo relevante. Devuelve la configuración nueva.
    /// </summary>
    public ApiConfig Reload()
    {
        lock (_gate)
        {
            var previous = _current;
            _current = Build();

            if (previous is not null && HasMeaningfulChange(previous, _current))
            {
                AppLogger.I("ApiConfigProvider",
                    "Configuracion recargada desde CloudLicense: " +
                    $"env={_current.Environment}, storeId={Describe(_current.StoreId)}, " +
                    $"baseUrl={_current.BaseUrl}, pins={_current.CertificatePins.Count}, " +
                    $"keyCambiada={!string.Equals(previous.SubscriptionKey, _current.SubscriptionKey, StringComparison.Ordinal)}");
            }

            LogValidation(_current);
            return _current;
        }
    }

    /// <summary>
    /// La tienda de la caja se LEE EN CADA BUILD, no se captura en el constructor:
    /// el instalador la elige con la app ya corriendo, y sin esto la pantalla
    /// seguiria diciendo "sin tienda" hasta reiniciar el proceso.
    /// </summary>
    private ApiConfig Build() =>
        ApiConfig.FromConfiguration(_configuration, _cloud, LeerTiendaDeLaCaja());

    private string? LeerTiendaDeLaCaja()
    {
        try
        {
            return _tiendaDeLaCaja?.StoreId;
        }
        catch (Exception ex)
        {
            // Si no se puede leer, la caja queda a lo que diga HioPosCloud. Nunca
            // se inventa una tienda.
            AppLogger.W("ApiConfigProvider",
                $"No se pudo leer la tienda configurada en la caja: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// QA C-3: deja constancia explícita de una configuración incoherente. No
    /// aborta aquí (la decisión de rechazar la transacción es de
    /// [MainActivity]), pero garantiza que quede registrado.
    /// </summary>
    private void LogValidation(ApiConfig config)
    {
        // Va SIEMPRE, válida o no: es la única línea que dice a nombre de qué
        // tienda va a operar esta caja. Ver [DescribirTienda].
        AppLogger.I("ApiConfigProvider", DescribirTienda(config));

        var problems = config.Validate();
        if (problems.Count == 0)
        {
            AppLogger.I("ApiConfigProvider",
                $"Configuracion valida (env={config.Environment}, " +
                $"pinning={(config.CertificatePins.Count > 0 ? "ON" : "OFF")}).");
            return;
        }

        foreach (var problem in problems)
            AppLogger.E("ApiConfigProvider", $"CONFIGURACION INVALIDA: {problem}");
    }

    /// <summary>
    /// QUE TIENDA ES ESTA CAJA, Y DE DONDE SALIO ESE DATO.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// LO QUE FALTABA PARA VER EL PROBLEMA DESDE UNA TIENDA
    /// ─────────────────────────────────────────────────────────────────────────
    /// El StoreId solo se registraba cuando CAMBIABA respecto al anterior, y aun
    /// asi enmascarado como "(definido)". En un arranque en frio no hay anterior,
    /// asi que no se registraba nunca. Resultado: una caja podia estar creando
    /// creditos a nombre de otra tienda y no habia UNA sola linea en el log que
    /// permitiera notarlo — habia que deducirlo conciliando en la plataforma de
    /// Sistecredito, semanas despues.
    ///
    /// El ORIGEN es lo que resuelve la pregunta cuando algo no cuadra: dice si la
    /// tienda la puso HioPosCloud, si la eligio el instalador, si coinciden, o si
    /// se contradicen.
    ///
    /// Y se NOMBRA la tienda ("037 · MULTIMARCA PUNTO CALLE 18") en vez de escribir
    /// el ObjectId: quien lee el log sabe de que tienda se habla sin ir a cotejar
    /// contra la hoja, y el identificador crudo solo aparece cuando NO figura en
    /// ella — que es justo el caso que hay que poder ver. Ver [ResolucionDeTienda.ParaElLog].
    /// </summary>
    private static string DescribirTienda(ApiConfig config)
    {
        // Se dice explicitamente si el identificador VIAJA o no. En sandbox no se
        // manda —Credinet de pruebas no conoce las tiendas de la hoja y responde
        // StoreNotFound— y sin esta linea alguien podria pasar una tarde buscando
        // por que el storeId "no llega".
        var viaja = string.IsNullOrWhiteSpace(config.StoreId)
            ? "NO se envia a Credinet (sandbox)"
            : "se envia a Credinet";

        return $"{config.Tienda.ParaElLog()} nombre='{config.StoreName}', " +
               $"env={config.Environment}, storeId {viaja}.";
    }

    private static bool HasMeaningfulChange(ApiConfig a, ApiConfig b) =>
        !string.Equals(a.SubscriptionKey, b.SubscriptionKey, StringComparison.Ordinal)
        || !string.Equals(a.BaseUrl, b.BaseUrl, StringComparison.Ordinal)
        || !string.Equals(a.StoreId, b.StoreId, StringComparison.Ordinal)
        || !string.Equals(a.Environment, b.Environment, StringComparison.Ordinal)
        || a.CertificatePins.Count != b.CertificatePins.Count;

    /// <summary>No se loguean valores potencialmente identificatorios completos.</summary>
    private static string Describe(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(vacio)" : "(definido)";
}
