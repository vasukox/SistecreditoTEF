using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Views;

namespace SistecreditoTEF.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // HU8-973: rutas del flujo registradas para navegación por PUSH.
        // Se navega con rutas relativas (ver AppRoutes) → el back hace pop.
        Routing.RegisterRoute(AppRoutes.CapturaCedula,     typeof(CapturaCedulaPage));
        Routing.RegisterRoute(AppRoutes.ValidacionCliente, typeof(ValidacionClientePage));
        Routing.RegisterRoute(AppRoutes.SeleccionCuotas,   typeof(SeleccionCuotasPage));
        Routing.RegisterRoute(AppRoutes.Otp,               typeof(OtpPage));
        Routing.RegisterRoute(AppRoutes.Confirmacion,      typeof(ConfirmacionPage));
        Routing.RegisterRoute(AppRoutes.CreditosActivos,   typeof(CreditosActivosPage));
        Routing.RegisterRoute(AppRoutes.Pago,              typeof(PagoPage));
        Routing.RegisterRoute(AppRoutes.ReciboPago,        typeof(ReciboPagoPage));
    }
}
