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
    private readonly object _gate = new();
    private ApiConfig? _current;

    public ApiConfigProvider(IConfiguration configuration, ICloudConfig cloud)
    {
        _configuration = configuration;
        _cloud = cloud;
    }

    /// <summary>Configuración vigente. Se construye al primer acceso.</summary>
    public ApiConfig Current
    {
        get
        {
            lock (_gate)
            {
                return _current ??= Build();
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

    private ApiConfig Build() => ApiConfig.FromConfiguration(_configuration, _cloud);

    /// <summary>
    /// QA C-3: deja constancia explícita de una configuración incoherente. No
    /// aborta aquí (la decisión de rechazar la transacción es de
    /// [MainActivity]), pero garantiza que quede registrado.
    /// </summary>
    private static void LogValidation(ApiConfig config)
    {
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
