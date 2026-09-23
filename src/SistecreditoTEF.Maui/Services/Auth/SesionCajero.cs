namespace SistecreditoTEF.Maui.Services.Auth;

/// <summary>
/// Quien esta operando el flujo de abonos en este momento.
///
/// Singleton en DI, y SOLO en memoria: se pierde al cerrar el proceso, que es lo
/// correcto —una sesion que sobrevive al cierre significa que el proximo que abra
/// la app cobra con la identidad del cajero anterior—.
/// </summary>
public interface ISesionCajero
{
    Cajero? Actual { get; }

    /// <summary>Nombre para la traza y para el <c>userName</c> que va a Credinet.</summary>
    string NombreParaAuditoria { get; }

    void Iniciar(Cajero cajero);
    void Cerrar();
}

public sealed class SesionCajero : ISesionCajero
{
    private readonly object _gate = new();
    private Cajero? _actual;

    public Cajero? Actual { get { lock (_gate) return _actual; } }

    /// <summary>
    /// Nombre del cajero identificado. El respaldo existe para no dejar la traza
    /// vacia si algo llega aca sin sesion; en el flujo de abonos no deberia pasar,
    /// porque la pantalla de ingreso es obligatoria.
    ///
    /// Usa [Cajero.NombreVisible] y no [Cajero.Nombre]: el nombre es OPCIONAL al dar
    /// de alta un cajero, asi que Nombre puede ser "" —no null— y "??" no lo
    /// atraparia. Esa distincion, en el camino equivalente de [PagoViewModel], mando
    /// un userName vacio a Credinet y dejo la caja sin poder cobrar abonos.
    /// </summary>
    public string NombreParaAuditoria
    {
        get
        {
            lock (_gate)
            {
                var nombre = _actual?.NombreVisible;
                return string.IsNullOrWhiteSpace(nombre) ? "Cajero sin identificar" : nombre;
            }
        }
    }

    public void Iniciar(Cajero cajero) { lock (_gate) _actual = cajero; }

    public void Cerrar() { lock (_gate) _actual = null; }
}
