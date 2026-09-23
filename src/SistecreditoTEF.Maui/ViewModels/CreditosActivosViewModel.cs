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

                        // El comprobante del abono debe llevar el nombre del cliente,
                        // y getactivecredits NO lo devuelve (solo idDocument). Se
                        // consulta aparte, en modo BEST-EFFORT: si falla, el abono
                        // sigue su curso y el comprobante sale con la cedula y sin
                        // el nombre. Nunca debe bloquear un recaudo.
                        await CargarNombreDelClienteAsync(docLimpio);
                    }
                    break;
                case ApiResult<List<ActiveCredit>>.Failure<List<ActiveCredit>> f:
                    // Traducido, no crudo. Asignaba [UserMessage] directo y en la
                    // pantalla se leia "CREDINET: CreditsNotFound" — ingles tecnico
                    // con el prefijo del proveedor, sin decirle al cajero que hacer.
                    AppLogger.W("CreditosActivosViewModel",
                        $"getactivecredits rechazado: {f.Cause.UserMessage}");
                    ErrorMessage = FriendlyMessage.FromApiError(f.Cause);
                    Status = Estado.Error;
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.E("CreditosActivosViewModel",
                "Excepcion inesperada buscando creditos activos", ex);
            // El detalle tecnico va al log, no a la pantalla: "Error inesperado:
            // Object reference not set..." no le sirve a nadie en una caja.
            ErrorMessage = "No pudimos consultar los creditos. Revisa la conexion e " +
                           "intenta de nuevo.";
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

    /// <summary>
    /// Trae el nombre del cliente para que aparezca en el comprobante del abono.
    ///
    /// <c>getactivecredits</c> devuelve el documento pero no el nombre, asi que se
    /// consulta <c>getCreditLimitClient</c>. Es BEST-EFFORT a proposito: un cliente
    /// puede tener creditos activos y a la vez no ser consultable por ese endpoint
    /// (sin cupo, bloqueado, etc.). Si falla, se registra y se sigue: el
    /// comprobante saldra con la cedula y sin el nombre, pero el recaudo se hace.
    /// </summary>
    private async Task CargarNombreDelClienteAsync(string documento)
    {
        try
        {
            var result = await service.ValidarClienteAsync(TipoDocumento, documento);
            if (result is ApiResult<Client>.Ok<Client> ok)
            {
                state.SetValidatedClient(ok.Data);
                AppLogger.I("CreditosActivosViewModel",
                    $"Datos del cliente cargados para el comprobante: {PiiMask.Name(ok.Data.FullName)}.");
            }
            else
            {
                AppLogger.W("CreditosActivosViewModel",
                    "No se pudo obtener el nombre del cliente; el comprobante saldra " +
                    "con la cedula solamente.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.W("CreditosActivosViewModel",
                $"Fallo la consulta del nombre del cliente (no bloquea el abono): {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SeleccionarAsync(ActiveCredit credito)
    {
        if (credito is null) return;
        state.SetSelectedCredit(credito);
        await nav.GoToPagoAsync(credito);
    }
}
