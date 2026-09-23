namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// Fuente de la configuración vigente de Credinet.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUÉ EXISTE ESTA INTERFAZ
/// ─────────────────────────────────────────────────────────────────────────────
/// Los consumidores necesitan leer la configuración **en el momento de usarla**,
/// no una copia capturada al arrancar el contenedor: los parámetros de
/// CloudLicense llegan en el INITIALIZE, que puede ocurrir después de que el
/// contenedor resolvió el servicio.
///
/// El intento anterior de resolver esto fue dar a [CredinetRepository] y a
/// [AuthInterceptor] DOS constructores —uno con el proveedor recargable y otro
/// con un <c>ApiConfig</c> fijo "para tests"—. Eso provoca
/// <c>AmbiguousConstructorException</c> en tiempo de ejecución:
/// Microsoft.Extensions.DependencyInjection no puede elegir entre dos
/// constructores con la misma cantidad de parámetros resolubles cuando ninguno es
/// subconjunto del otro, y la app crashea en el primer <c>GetService</c>.
///
/// Con esta interfaz hay **un solo constructor** por servicio:
///   - en la app se inyecta [ApiConfigProvider], que relee CloudLicense;
///   - en los tests se inyecta [StaticApiConfigSource], con un valor fijo.
///
/// Regla general que se desprende: un servicio destinado al contenedor de DI debe
/// tener exactamente UN constructor público. Está protegido por
/// <c>InyeccionDeDependenciasTests</c>.
/// </summary>
public interface IApiConfigSource
{
    /// <summary>Configuración vigente en este instante.</summary>
    ApiConfig Current { get; }
}

/// <summary>
/// Fuente de configuración con un valor fijo. Para pruebas y para escenarios en
/// los que la configuración no cambia durante la vida del objeto.
/// </summary>
public sealed class StaticApiConfigSource : IApiConfigSource
{
    public StaticApiConfigSource(ApiConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Current = config;
    }

    public ApiConfig Current { get; }
}
