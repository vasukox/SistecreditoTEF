using SistecreditoTEF.Maui.Services.Hiopos;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Constantes de rutas de navegacion Shell.
///
/// DRY: cualquier referencia a "capturaCedula", "//otp", etc. usa
/// estas constantes. Antes estaban hardcodeadas en code-behinds y VMs.
///
/// Formato Shell URIs:
///   "//ruta"      - ruta absoluta (limpia el stack)
///   "ruta"        - ruta relativa (push)
///   ".."          - pop
/// </summary>
public static class AppRoutes
{
    // HU8-973: rutas RELATIVAS (push) para que se arme el stack y el botón
    // "atrás" haga pop en vez de cerrar la app. Home queda absoluta ("//")
    // porque es el reset del stack (al finalizar / volver al inicio).
    public const string Home              = "//home";
    public const string CapturaCedula     = "capturaCedula";
    public const string ValidacionCliente = "validacionCliente";
    public const string SeleccionCuotas   = "seleccionCuotas";
    public const string Otp               = "otp";
    public const string Confirmacion      = "confirmacion";
    public const string CreditosActivos   = "creditosActivos";
    public const string Pago              = "pago";
    public const string ReciboPago        = "reciboPago";

    /// <summary>
    /// Claves de parametros de QueryProperty.
    /// </summary>
    public static class Params
    {
        public const string Payment      = "payment";
        public const string Client       = "client";
    }
}
