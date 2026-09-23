using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Views;

namespace SistecreditoTEF.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // HU8-973: rutas del flujo registradas para navegación por PUSH.
        // Se navega con rutas relativas (ver AppRoutes) → el back hace pop.
        // Lista compartida con el APK de Abonos (DRY): ver RouteRegistrar.
        RouteRegistrar.RegisterAll();
    }

    // ------------------------------------------------------------------
    // Menu de hamburguesa
    // ------------------------------------------------------------------
    // Los handlers son async void (lo exige el evento Clicked), asi que TODO va
    // envuelto en try/catch: una excepcion que se escape de un async void sube al
    // SynchronizationContext de Android y mata el proceso.

    /// <summary>
    /// Abre la configuracion de cajeros. La pantalla destino pide el PIN de
    /// administrador antes de mostrar nada, asi que el acceso no depende de que
    /// esta opcion este escondida.
    /// </summary>
    private async void OnConfiguracionClicked(object? sender, EventArgs e)
    {
        try
        {
            FlyoutIsPresented = false;
            await GoToAsync(AppRoutes.AdminCajeros);
        }
        catch (Exception ex)
        {
            AppLogger.E("AppShell", "No se pudo abrir la configuracion de cajeros.", ex);
        }
    }

    /// <summary>
    /// Cierra la sesion del cajero y vuelve al ingreso.
    ///
    /// Es lo que permite entregar el terminal a otra persona sin reiniciar la app:
    /// sin esto, el siguiente abono se registraria a nombre del cajero anterior.
    /// </summary>
    private async void OnCerrarSesionClicked(object? sender, EventArgs e)
    {
        try
        {
            FlyoutIsPresented = false;

            var sesion = IPlatformApplication.Current?.Services
                .GetService(typeof(ISesionCajero)) as ISesionCajero;
            sesion?.Cerrar();

            AppLogger.I("AppShell", "Sesion del cajero cerrada desde el menu.");
            await GoToAsync(AppRoutes.IngresoCajero);
        }
        catch (Exception ex)
        {
            AppLogger.E("AppShell", "No se pudo cerrar la sesion del cajero.", ex);
        }
    }
}
