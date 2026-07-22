using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla raiz del kiosko (HomePage). Dos opciones grandes:
///   - Comprar a credito: navega al flujo nuevo (CapturaCedula)
///   - Pagar credito: navega al flujo de pago (CreditosActivos)
///
/// HU8-973 fix: usa [INavigationService] inyectado en lugar de
/// [Shell.Current.GoToAsync] directo. Esto evita crashear si Shell no esta
/// listo (cold start, navegacion concurrente, MainThread marshalling).
/// </summary>
public partial class HomeViewModel(
    INavigationService nav) : ObservableObject
{
    [RelayCommand]
    private async Task GoNuevoCreditoAsync()
    {
        try
        {
            await nav.GoToCapturaCedulaAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("HomeViewModel", "Error navegando a CapturaCedula", ex);
        }
    }

    [RelayCommand]
    private async Task GoPagarCreditoAsync()
    {
        try
        {
            await nav.GoToCreditosActivosAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("HomeViewModel", "Error navegando a CreditosActivos", ex);
        }
    }
}