using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Enums;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 1: captura de documento del cliente.
/// MVVM puro: state en el VM, View solo bindea.
/// </summary>
public partial class CapturaCedulaViewModel(
    SistecreditoService service,
    ITransactionStateStore state) : ObservableObject
{
    public enum Estado { Idle, Loading, Success, Error, Validation }

    [ObservableProperty]
    private Estado status = Estado.Idle;

    [ObservableProperty]
    private Client? client;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? validationMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ValidarCommand))]
    private string numeroDocumento = string.Empty;

    [ObservableProperty]
    private DocumentType tipoDocumento = DocumentType.CedulaCiudadania;

    public bool IsLoading => Status == Estado.Loading;
    public bool HasError => Status == Estado.Error;
    public bool HasValidation => Status == Estado.Validation;
    public bool IsSuccess => Status == Estado.Success;

    partial void OnStatusChanged(Estado value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasValidation));
        OnPropertyChanged(nameof(IsSuccess));
    }

    /// <summary>
    /// HU-134: autocompleta la cedula desde el cliente que HioPos tiene
    /// asignado a la venta (doc §21: Customer.FiscalId), leido inline del
    /// documento de la transaccion.
    ///
    /// Cubre los dos escenarios:
    ///  - Con cliente asignado -> pre-llena cedula y tipo (editable).
    ///  - Sin cliente (venta anonima) -> deja el campo vacio para captura
    ///    manual. NO bloquea la facturacion.
    ///
    /// La llama la View en OnAppearing.
    /// </summary>
    public void Inicializar()
    {
        var doc = state.ActiveDocument;

        // Log DIAGNOSTICO (sin PII): que campos trae el Customer y el docType
        // crudo. Sirve para confirmar la estructura real del XML de HioPos.
        var keys = doc?.Customer?.Fields.Select(f => f.Key) ?? Enumerable.Empty<string>();
        AppLogger.I("CapturaCedulaViewModel",
            $"Customer del documento: campos=[{string.Join(",", keys)}], " +
            $"docType='{doc?.CustomerFiscalDocType ?? "-"}'.");

        // Si el cajero ya escribio algo (o volvio a la pantalla), no pisar.
        if (!string.IsNullOrWhiteSpace(NumeroDocumento))
            return;

        var fiscalId = doc?.CustomerFiscalId;
        if (string.IsNullOrWhiteSpace(fiscalId))
        {
            AppLogger.I("CapturaCedulaViewModel",
                "Sin cliente asignado en HioPos -> captura manual.");
            return;
        }

        // Misma normalizacion que ValidarAsync (CREDINET exige sin separadores).
        var limpio = fiscalId.Trim()
            .Replace(".", "").Replace(",", "").Replace(" ", "").Replace("-", "");
        if (limpio.Length == 0)
            return;

        NumeroDocumento = limpio;
        TipoDocumento = MapDocType(doc!.CustomerFiscalDocType);
        AppLogger.I("CapturaCedulaViewModel",
            $"Cedula autocompletada desde HioPos: {Mask(limpio)} (tipo={TipoDocumento}).");
    }

    /// <summary>
    /// Mapeo del tipo de documento de HioPos al enum. Por defecto CC (caso
    /// dominante). Solo CE si HioPos lo indica explicitamente. El campo queda
    /// editable, y el log de Inicializar registra el codigo crudo para refinar
    /// este mapeo cuando confirmemos los valores reales.
    /// </summary>
    private static DocumentType MapDocType(string? raw)
    {
        var r = raw?.Trim();
        if (string.Equals(r, "CE", StringComparison.OrdinalIgnoreCase))
            return DocumentType.CedulaExtranjeria;
        return DocumentType.CedulaCiudadania;
    }

    /// <summary>Enmascara la cedula para logs (deja solo los ultimos 4).</summary>
    private static string Mask(string doc) =>
        doc.Length <= 4 ? new string('*', doc.Length)
                        : new string('*', doc.Length - 4) + doc[^4..];

    [RelayCommand(CanExecute = nameof(CanValidar))]
    private async Task ValidarAsync()
    {
#if ANDROID
        Android.Util.Log.Info("CCVM", $"ENTER doc={NumeroDocumento} status={Status}");
#endif
        if (string.IsNullOrWhiteSpace(NumeroDocumento))
        {
            Status = Estado.Validation;
            ValidationMessage = FriendlyMessage.Validation("doc.empty");
            return;
        }

        // HU8-973: el teclado Numeric de Android permite el separador decimal,
        // asi que el cajero puede dejar un '.' (o espacios/guiones/comas) en la
        // cedula. Un idDocument con separadores hace que CREDINET responda
        // 224 CustomerNotFound. El manual pide la cedula "sin separadores":
        // normalizamos antes de validar.
        var idLimpio = NumeroDocumento.Trim()
            .Replace(".", "").Replace(",", "").Replace(" ", "").Replace("-", "");
        if (idLimpio != NumeroDocumento)
            NumeroDocumento = idLimpio;

        if (string.IsNullOrEmpty(NumeroDocumento))
        {
            Status = Estado.Validation;
            ValidationMessage = FriendlyMessage.Validation("doc.invalid");
            return;
        }

        if (NumeroDocumento.Length < 4)
        {
            Status = Estado.Validation;
            ValidationMessage = FriendlyMessage.Validation("doc.short");
            return;
        }

        Status = Estado.Loading;
        ErrorMessage = null;
        ValidationMessage = null;

#if ANDROID
        Android.Util.Log.Info("CCVM", $"calling service.ValidarClienteAsync");
#endif
        try
        {
            // HU8-973: NO usar ConfigureAwait(false) aqui. La continuacion
            // actualiza bindings y navega con Shell (trabajo de UI); debe
            // volver al hilo de UI o la navegacion se cuelga/crashea en Android.
            var result = await service.ValidarClienteAsync(TipoDocumento, NumeroDocumento);
#if ANDROID
            Android.Util.Log.Info("CCVM", $"await done, type={result.GetType().Name}");
#endif
            switch (result)
            {
                case ApiResult<Client>.Ok<Client> ok:
                    Client = ok.Data;
                    Status = Estado.Success;
                    state.SetValidatedClient(ok.Data);
#if ANDROID
                    Android.Util.Log.Info("CCVM", "navigating to ValidacionCliente");
#endif
                    if (Shell.Current is not null)
                        await Shell.Current.GoToAsync(AppRoutes.ValidacionCliente);
                    break;
                case ApiResult<Client>.Failure<Client> f:
                    // Mensaje amigable al cajero (sin "CREDINET:", sin codigos HTTP).
                    ErrorMessage = FriendlyMessage.FromApiError(f.Cause);
                    Status = Estado.Error;
#if ANDROID
                    Android.Util.Log.Info("CCVM", $"Failure: {f.Cause.UserMessage}");
#endif
                    break;
            }
        }
        catch (Exception ex)
        {
#if ANDROID
            Android.Util.Log.Error("CCVM",
                $"EX {ex.GetType().Name}: {ex.Message}");
#endif
            AppLogger.E("CapturaCedulaViewModel",
                $"Excepcion inesperada validando {TipoDocumento.Code()} {NumeroDocumento}", ex);
            ErrorMessage = "Algo salio mal. Intenta de nuevo.";
            Status = Estado.Error;
        }
#if ANDROID
        Android.Util.Log.Info("CCVM", $"EXIT status={Status}");
#endif
    }

    private bool CanValidar() => !string.IsNullOrWhiteSpace(NumeroDocumento) && Status != Estado.Loading;
}
