using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Views;

/// <summary>
/// HU8-973: registro de rutas de navegación Shell, COMPARTIDO por las dos apps
/// (DRY). Antes cada AppShell repetía la misma lista de <c>RegisterRoute</c>,
/// con riesgo de drift al agregar una pantalla en una app y olvidarla en la otra.
/// </summary>
public static class RouteRegistrar
{
    /// <summary>Registra todas las rutas del flujo (abonos + crédito standalone).</summary>
    public static void RegisterAll()
    {
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
