using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Abstraccion de navegacion Shell.
///
/// POR QUE EXISTE (V3): las rutas estaban hardcodeadas en 8 code-behinds.
/// Ahora viven en [AppRoutes] y este servicio centraliza el "como".
/// Tambien permite mockear navegacion en tests.
/// </summary>
public interface INavigationService
{
    Task GoToCapturaCedulaAsync();
    Task GoToValidacionClienteAsync();
    Task GoToSeleccionCuotasAsync();
    Task GoToOtpAsync();
    Task GoToConfirmacionAsync(Credit credit);
    Task GoToPagoAsync(ActiveCredit credit);
    Task GoToReciboPagoAsync(Payment payment);
    Task GoToCreditosActivosAsync();
    Task GoToHomeAsync();

    // ---- Ingreso de cajeros (solo abonos) ----
    Task GoToConfigurarAdminAsync();
    Task GoToIngresoCajeroAsync();
    Task GoToAdminCajerosAsync();
    Task GoToReplicacionAsync();
}
