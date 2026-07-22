using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Implementacion de [INavigationService] sobre MAUI Shell.
///
/// V3 (DRY): rutas centralizadas en [AppRoutes] - antes estaban
/// hardcodeadas en 8 archivos.
/// </summary>
public sealed class ShellNavigationService : INavigationService
{
    public Task GoToCapturaCedulaAsync() =>
        Shell.Current.GoToAsync(AppRoutes.CapturaCedula);

    public Task GoToValidacionClienteAsync() =>
        Shell.Current.GoToAsync(AppRoutes.ValidacionCliente);

    public Task GoToSeleccionCuotasAsync() =>
        Shell.Current.GoToAsync(AppRoutes.SeleccionCuotas);

    public Task GoToOtpAsync() =>
        Shell.Current.GoToAsync(AppRoutes.Otp);

    public Task GoToConfirmacionAsync(Credit credit) =>
        Shell.Current.GoToAsync(AppRoutes.Confirmacion);

    public Task GoToPagoAsync(ActiveCredit credit)
    {
        var parameters = new Dictionary<string, object>
        {
            { AppRoutes.Params.Payment, credit }
        };
        return Shell.Current.GoToAsync(AppRoutes.Pago, parameters);
    }

    public Task GoToReciboPagoAsync(Payment payment)
    {
        var parameters = new Dictionary<string, object>
        {
            { AppRoutes.Params.Payment, payment }
        };
        return Shell.Current.GoToAsync(AppRoutes.ReciboPago, parameters);
    }

    public Task GoToCreditosActivosAsync() =>
        Shell.Current.GoToAsync(AppRoutes.CreditosActivos);

    public Task GoToHomeAsync() =>
        Shell.Current.GoToAsync(AppRoutes.Home);
}
