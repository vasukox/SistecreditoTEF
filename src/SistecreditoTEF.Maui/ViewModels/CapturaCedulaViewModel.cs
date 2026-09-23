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

    // ------------------------------------------------------------------
    // Salida al flujo de abonos: RETIRADA
    // ------------------------------------------------------------------
    // Habia un boton "Pagar credito (abono)" que desde la venta llevaba al
    // recaudo. Se quito porque los abonos salieron del flujo de HioPos: ahora se
    // hacen abriendo el APK desde el icono. En la venta esta pantalla es solo para
    // solicitar credito.
    //
    // El ruteo automatico de MainActivity (TRANSACTION sin documento -> recaudo) se
    // dejo en pie a proposito: si HioPos igual dispara una entrada de caja, sigue
    // funcionando en vez de dejar al cajero en la captura de cliente.

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

        // Se re-sincroniza con el cliente de la venta CADA VEZ que se entra a la
        // pantalla, salvo que el cajero haya escrito una cedula distinta a mano.
        //
        // Antes bastaba que el campo tuviera algo para no tocarlo:
        //
        //     if (!string.IsNullOrWhiteSpace(NumeroDocumento)) return;
        //
        // Con eso, al volver atras y entrar de nuevo quedaba pegada la cedula de
        // antes. Si en el medio HioPos habia cambiado de venta (el cajero volvio
        // atras y eligio Sistecredito otra vez), el modulo seguia mostrando el
        // cliente equivocado.
        //
        // Se distingue "lo puso el autocompletado" de "lo escribio el cajero"
        // comparando contra el ultimo valor autocompletado: si coincide, nadie lo
        // toco y se puede refrescar; si difiere, se respeta lo tecleado.
        var editadoPorElCajero =
            !string.IsNullOrWhiteSpace(NumeroDocumento)
            && !string.Equals(NumeroDocumento, _ultimaCedulaAutocompletada, StringComparison.Ordinal);

        if (editadoPorElCajero)
        {
            AppLogger.I("CapturaCedulaViewModel",
                $"Se conserva la cedula ingresada a mano ({Mask(NumeroDocumento)}); " +
                "no se sobreescribe con la del documento.");
            return;
        }

        var fiscalId = doc?.CustomerFiscalId;
        if (string.IsNullOrWhiteSpace(fiscalId))
        {
            AppLogger.I("CapturaCedulaViewModel",
                "Sin cliente asignado en HioPos -> captura manual.");
            return;
        }

        // Misma normalizacion que ValidarAsync (CREDINET exige sin separadores).
        var limpio = DocumentNumber.Normalize(fiscalId);

        // GUARD DEL CLIENTE GENERICO: si el documento de la venta trae el marcador
        // de cliente anonimo del POS (222222222222 y similares), NO se
        // autocompleta.
        //
        // Autocompletar el generico es peor que dejar el campo vacio: el cajero ve
        // un numero ya puesto, no lo revisa, y valida contra Credinet a una
        // persona que no es la que esta comprando. En el terminal se veia asi: se
        // facturaba con una cedula que empieza por 430 y la pantalla traia
        // 222222222222.
        if (DocumentNumber.IsGenericPlaceholder(limpio))
        {
            AppLogger.W("CapturaCedulaViewModel",
                $"El cliente del documento de HioPos es el GENERICO ({Mask(limpio)}); " +
                "no se autocompleta. El cajero debe ingresar la cedula real.");
            return;
        }

        NumeroDocumento = limpio;
        _ultimaCedulaAutocompletada = limpio;
        TipoDocumento = MapDocType(doc!.CustomerFiscalDocType);
        AppLogger.I("CapturaCedulaViewModel",
            $"Cedula autocompletada desde HioPos: {Mask(limpio)} (tipo={TipoDocumento}).");
    }

    /// <summary>
    /// Último valor puesto por el autocompletado. Sirve para distinguir "el campo
    /// lo llenó el módulo" de "lo escribió el cajero", y así poder re-sincronizar
    /// sin pisar lo que la persona tecleó.
    /// </summary>
    private string? _ultimaCedulaAutocompletada;

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

    /// <summary>
    /// Enmascara la cedula para logs. QA A-1: delega en [PiiMask] para que el
    /// enmascarado sea uno solo en todo el proyecto (esta clase tenia su propia
    /// copia y la aplicaba en una linea pero no en otras dos).
    /// </summary>
    private static string Mask(string? doc) => PiiMask.Document(doc);

    [RelayCommand(CanExecute = nameof(CanValidar))]
    private async Task ValidarAsync()
    {
        // QA A-1: la traza de diagnostico va bajo #if DEBUG, no #if ANDROID.
        // Antes se enviaba en RELEASE con la cedula EN CLARO al logcat, junto con
        // otros cinco Log.Info del mismo metodo. En terminales POS con adb
        // habilitado (habitual para soporte), eso deja el historico de cedulas
        // atendidas al alcance de cualquiera con acceso USB. Ley 1581 de 2012.
#if ANDROID && DEBUG
        Android.Util.Log.Info("CCVM", $"ENTER doc={Mask(NumeroDocumento)} status={Status}");
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
        // normalizamos antes de validar. Una sola implementacion, compartida con
        // el autocompletado.
        var idLimpio = DocumentNumber.Normalize(NumeroDocumento);
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

        try
        {
            // HU8-973: NO usar ConfigureAwait(false) aqui. La continuacion
            // actualiza bindings y navega con Shell (trabajo de UI); debe
            // volver al hilo de UI o la navegacion se cuelga/crashea en Android.
            var result = await service.ValidarClienteAsync(TipoDocumento, NumeroDocumento);

            switch (result)
            {
                case ApiResult<Client>.Ok<Client> ok:
                    Client = ok.Data;
                    Status = Estado.Success;
                    state.SetValidatedClient(ok.Data);
                    if (Shell.Current is not null)
                        await Shell.Current.GoToAsync(AppRoutes.ValidacionCliente);
                    break;
                case ApiResult<Client>.Failure<Client> f:
                    // Mensaje amigable al cajero (sin "CREDINET:", sin codigos HTTP).
                    ErrorMessage = FriendlyMessage.FromApiError(f.Cause);
                    Status = Estado.Error;
                    AppLogger.W("CapturaCedulaViewModel",
                        $"Validacion rechazada para {Mask(NumeroDocumento)}: {f.Cause.UserMessage}");
                    break;
            }
        }
        catch (Exception ex)
        {
            // QA A-1: cedula ENMASCARADA. Antes este log volcaba el numero
            // completo al logcat.
            AppLogger.E("CapturaCedulaViewModel",
                $"Excepcion inesperada validando {TipoDocumento.Code()} {Mask(NumeroDocumento)}", ex);
            ErrorMessage = "Algo salio mal. Intenta de nuevo.";
            Status = Estado.Error;
        }
    }

    private bool CanValidar() => !string.IsNullOrWhiteSpace(NumeroDocumento) && Status != Estado.Loading;
}
