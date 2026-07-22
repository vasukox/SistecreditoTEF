using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 2: confirmacion del cliente validado.
/// </summary>
public partial class ValidacionClienteViewModel(
    ITransactionStateStore state,
    INavigationService nav) : ObservableObject
{
    [ObservableProperty]
    private Client? cliente;

    public string ClientName     => Cliente?.FullName ?? string.Empty;
    public string ClientEmail    => Cliente?.Email ?? string.Empty;
    public string ClientMobile   => Cliente?.Mobile ?? string.Empty;
    public string CreditLimit    => Cliente?.CreditLimit.ToColombianCurrency() ?? "$ 0";
    public string AvailableLimit => Cliente?.AvailableCreditLimit.ToColombianCurrency() ?? "$ 0";
    public string Estado         => Cliente?.IsActive == true ? "ACTIVO" : "INACTIVO";

    partial void OnClienteChanged(Client? value)
    {
        OnPropertyChanged(nameof(ClientName));
        OnPropertyChanged(nameof(ClientEmail));
        OnPropertyChanged(nameof(ClientMobile));
        OnPropertyChanged(nameof(CreditLimit));
        OnPropertyChanged(nameof(AvailableLimit));
        OnPropertyChanged(nameof(Estado));
    }

    public void Inicializar()
    {
        Cliente = state.ValidatedClient;
    }

    [RelayCommand]
    private Task ContinuarAsync() => nav.GoToSeleccionCuotasAsync();
}
