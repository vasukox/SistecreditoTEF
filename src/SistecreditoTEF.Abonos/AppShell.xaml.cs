using SistecreditoTEF.Maui.Views;

namespace SistecreditoTEF.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // HU8-973: rutas del flujo standalone (abonos + nuevo credito manual).
        // Lista compartida con el APK TEF (DRY): ver RouteRegistrar.
        RouteRegistrar.RegisterAll();
    }
}