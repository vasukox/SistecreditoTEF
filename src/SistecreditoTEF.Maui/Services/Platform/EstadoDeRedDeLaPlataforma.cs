using Microsoft.Maui.Networking;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// [IEstadoDeRed] sobre el servicio de conectividad de Android.
///
/// El permiso ACCESS_NETWORK_STATE ya esta declarado en el manifiesto; sin el,
/// esta clase devolveria "en linea" siempre en vez de fallar (ver abajo).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ANTE UN ERROR SE DICE "EN LINEA"
/// ─────────────────────────────────────────────────────────────────────────────
/// Los dos errores posibles no cuestan lo mismo. Decir "sin conexion" teniendo red
/// hace que el cajero no empiece un cobro que si podia hacer: pierde una venta por
/// un adorno de la cabecera. Decir "en linea" sin red solo hace que el error
/// aparezca un paso mas adelante, que es donde aparecia antes de que esto
/// existiera.
///
/// Asi que ante la duda se calla y deja operar.
/// </summary>
public sealed class EstadoDeRedDeLaPlataforma : IEstadoDeRed, IDisposable
{
    private bool _enganchado;

    public EstadoDeRedDeLaPlataforma()
    {
        try
        {
            Connectivity.Current.ConnectivityChanged += OnConectividadCambio;
            _enganchado = true;
        }
        catch (Exception ex)
        {
            // Sin el evento el indicador queda estatico hasta que la pantalla se
            // vuelva a dibujar. Degrada; no rompe.
            AppLogger.W("EstadoDeRed",
                $"No se pudo escuchar los cambios de conectividad: {ex.Message}");
        }
    }

    public bool HayConexion
    {
        get
        {
            try
            {
                // ConstrainedInternet cuenta: es una red con portal cautivo o
                // limitada, y desde el punto de vista del cajero hay que intentar
                // igual. El que decide si de verdad se puede cobrar es el timeout
                // de la llamada, no esta propiedad.
                return Connectivity.Current.NetworkAccess
                    is NetworkAccess.Internet or NetworkAccess.ConstrainedInternet;
            }
            catch (Exception ex)
            {
                AppLogger.W("EstadoDeRed", $"No se pudo leer el estado de la red: {ex.Message}");
                return true;
            }
        }
    }

    public event EventHandler? Cambio;

    private void OnConectividadCambio(object? sender, ConnectivityChangedEventArgs e)
    {
        try
        {
            AppLogger.I("EstadoDeRed",
                $"Cambio la conectividad: acceso={e.NetworkAccess}.");
            Cambio?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Los suscriptores son controles de UI. Si uno falla al repintarse, el
            // resto tiene que enterarse igual y la app no puede caerse por eso.
            AppLogger.W("EstadoDeRed", $"Fallo avisando el cambio de red: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_enganchado) return;
        try
        {
            Connectivity.Current.ConnectivityChanged -= OnConectividadCambio;
        }
        catch (Exception ex)
        {
            AppLogger.W("EstadoDeRed", $"No se pudo soltar el evento: {ex.Message}");
        }
        _enganchado = false;
    }
}
