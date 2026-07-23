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
}
