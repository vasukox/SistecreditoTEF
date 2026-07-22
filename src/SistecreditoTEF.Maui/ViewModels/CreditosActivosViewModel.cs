using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Enums;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 6: lista de creditos activos del cliente.
/// MVVM puro: expone ObservableCollection para BindableLayout en XAML.
/// </summary>
public partial class CreditosActivosViewModel(
    SistecreditoService service,
    INavigationService nav,
    ITransactionStateStore state) : ObservableObject
{
    public enum Estado { Idle, Loading, Success, Empty, Error }

    [ObservableProperty]
    private Estado status = Estado.Idle;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private DocumentType tipoDocumento = DocumentType.CedulaCiudadania;

    [ObservableProperty]
    private string numeroDocumento = string.Empty;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<ActiveCredit> creditos = new();

    public bool IsLoading => Status == Estado.Loading;
    public bool IsEmpty   => Status == Estado.Empty;
    public bool HasError  => Status == Estado.Error;
    public bool HasResults => Status == Estado.Success && Creditos.Count > 0;

    partial void OnStatusChanged(Estado value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasResults));
    }

    partial void OnCreditosChanged(System.Collections.ObjectModel.ObservableCollection<ActiveCredit> value)
    {
        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand(CanExecute = nameof(CanBuscar))]
    private async Task BuscarAsync()
    {
        // HU8-973 BugFix #4 + #6: feedback visible al cajero si el documento
        // esta vacio o mal formateado (quitamos puntos, comas, espacios,
        // guiones). Antes: retorno silencioso que parecia "no hace nada".
        if (string.IsNullOrWhiteSpace(NumeroDocumento))
        {
            ErrorMessage = "Ingresa el numero de documento del cliente.";
            Status = Estado.Error;
            return;
        }

        var docLimpio = SanitizarDocumento(NumeroDocumento);
        if (docLimpio.Length < 4)
        {
            ErrorMessage = "El numero de documento es muy corto.";
            Status = Estado.Error;
            return;
        }
        NumeroDocumento = docLimpio;  // normalizamos lo que ve el cajero

        Status = Estado.Loading;
        Creditos.Clear();
        ErrorMessage = null;

        try
        {
            var result = await service.ObtenerCreditosActivosAsync(TipoDocumento, docLimpio);

            switch (result)
            {
                case ApiResult<List<ActiveCredit>>.Ok<List<ActiveCredit>> ok:
                    if (ok.Data.Count == 0)
                    {
                        Status = Estado.Empty;
                    }
                    else
                    {
                        foreach (var c in ok.Data) Creditos.Add(c);
                        Status = Estado.Success;
                    }
                    break;
                case ApiResult<List<ActiveCredit>>.Failure<List<ActiveCredit>> f:
                    ErrorMessage = f.Cause.UserMessage;
                    Status = Estado.Error;
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.E("CreditosActivosViewModel",
                "Excepcion inesperada buscando creditos activos", ex);
            ErrorMessage = $"Error inesperado: {ex.Message}";
            Status = Estado.Error;
        }
    }

    // HU8-973 BugFix #2: deshabilitamos el boton "Buscar" durante la carga
    // para evitar multiples requests en paralelo (cada tap generaba una
    // nueva llamada a getActiveCredits que competian entre si).
    private bool CanBuscar() => Status != Estado.Loading;

    // HU8-973 BugFix #6: quitar caracteres de formato del documento
    // (1.234.567-8 -> 12345678) para evitar errores de validacion en Credinet.
    private static string SanitizarDocumento(string doc) =>
        new string(doc.Where(char.IsDigit).ToArray());

    [RelayCommand]
    private async Task SeleccionarAsync(ActiveCredit credito)
    {
        if (credito is null) return;
        state.SetSelectedCredit(credito);
        await nav.GoToPagoAsync(credito);
    }
}
