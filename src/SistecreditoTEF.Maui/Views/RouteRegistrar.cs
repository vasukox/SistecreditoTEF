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
        // Home dejo de ser la raiz del Shell (ahora lo es SplashPage), asi que se
        // registra como ruta normal para seguir siendo alcanzable.
        Routing.RegisterRoute(AppRoutes.Menu,              typeof(HomePage));
        Routing.RegisterRoute(AppRoutes.CapturaCedula,     typeof(CapturaCedulaPage));
        Routing.RegisterRoute(AppRoutes.ValidacionCliente, typeof(ValidacionClientePage));
        Routing.RegisterRoute(AppRoutes.SeleccionCuotas,   typeof(SeleccionCuotasPage));
        Routing.RegisterRoute(AppRoutes.Otp,               typeof(OtpPage));
        Routing.RegisterRoute(AppRoutes.Confirmacion,      typeof(ConfirmacionPage));
        Routing.RegisterRoute(AppRoutes.CreditosActivos,   typeof(CreditosActivosPage));
        Routing.RegisterRoute(AppRoutes.Pago,              typeof(PagoPage));
        Routing.RegisterRoute(AppRoutes.ReciboPago,        typeof(ReciboPagoPage));

        // Ingreso de cajeros: solo aplica al flujo de abonos.
        Routing.RegisterRoute(AppRoutes.ConfiguracionInicio, typeof(ConfiguracionInicioPage));
        Routing.RegisterRoute(AppRoutes.ConfigurarAdmin,   typeof(ConfigurarAdminPage));
        Routing.RegisterRoute(AppRoutes.IngresoCajero,     typeof(IngresoCajeroPage));
        Routing.RegisterRoute(AppRoutes.AdminCajeros,      typeof(AdminCajerosPage));
        Routing.RegisterRoute(AppRoutes.Replicacion,       typeof(ReplicacionPage));

        // Que tienda es esta caja. Se llega al instalar y desde administracion.
        Routing.RegisterRoute(AppRoutes.Tienda,            typeof(TiendaPage));
    }
}
