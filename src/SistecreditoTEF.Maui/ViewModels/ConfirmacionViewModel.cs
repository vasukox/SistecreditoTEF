using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Hiopos.Models;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 5: confirmacion del credito creado + devolucion del resultado al POS.
/// </summary>
public partial class ConfirmacionViewModel(
    ITransactionStateStore state,
    ITransactionResultHandler resultHandler,
    ReceiptBuilder receiptBuilder,
    ModifyDocumentResultBuilder modifyDocBuilder,
    IStandaloneModeTracker standalone,
    IAuditLogger audit,
    ApiConfig config) : ObservableObject
{
    // PaymentMeanId del medio "Tarjeta", sobre el que se consolida Sistecredito en
    // una venta. NO se lee del documento porque al iniciar la transaccion
    // PaymentMeans viene vacio (el medio se agrega DESPUES de que respondemos); un
    // Id vacio hacia que el modulo fiscal reportara "error en consulta de folios".
    //
    // Ahora sale de [ApiConfig.PaymentMeanIdVenta] y ya no es una constante
    // repetida en dos ViewModels: se corrige por HioPosCloud sin recompilar. El
    // comentario anterior afirmaba que eso ya estaba hecho, y no lo estaba.

    /// <summary>
    /// Tolerancia al comparar el monto financiado con el que pidio HioPos.
    /// Un peso cubre diferencias de redondeo legitimas.
    /// </summary>
    private const long AmountToleranceCents = 100;

    [ObservableProperty]
    private Credit? credito;

    public string CreditId => Credito?.CreditId ?? string.Empty;
    public string CreditNumber => Credito?.CreditNumber.ToString(CultureInfo.InvariantCulture) ?? "0";
    public string MontoAprobado => Credito?.CreditValue.ToColombianCurrency() ?? "$ 0";
    public string Meses => $"{PlazoCuotas} cuotas mensuales";
    public string PrimeraFechaPago => DateHelper.CalcularPrimeraFechaEstimadaPago();

    /// <summary>QA M-7: nombre real de la tienda, no "Permoda" hardcodeado.</summary>
    public string Tienda => config.StoreName;

    public string Cajero =>
        SistecreditoService.ExtractSellerName(state.ActiveTransaction?.SellerData)
        ?? "Cajero Permoda";

    public string Cliente => state.ValidatedClient?.FullName ?? string.Empty;

    public bool IsSuccess => Credito is not null;

    /// <summary>
    /// Plazo autoritativo: el que devolvio la API (<c>Credit.Fees</c>), con
    /// fallback al que eligio el cajero.
    ///
    /// QA M-5: antes el voucher usaba <c>state.Months</c> y el
    /// ModifyDocumentResult usaba <c>Credito.Fees</c>. Dos fuentes para el mismo
    /// dato, que discrepan si la API ajusta el plazo.
    /// </summary>
    private int PlazoCuotas => Credito is { Fees: > 0 } c ? c.Fees : state.Months;

    partial void OnCreditoChanged(Credit? value)
    {
        OnPropertyChanged(nameof(CreditId));
        OnPropertyChanged(nameof(CreditNumber));
        OnPropertyChanged(nameof(MontoAprobado));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(Meses));
    }

    public void Inicializar()
    {
        Credito = state.CreatedCredit;
    }

    [RelayCommand]
    private void Finalizar()
    {
        // Modo standalone: no hay HioPos esperando un setResult, se cierra la app.
        if (standalone.IsStandalone)
        {
            state.Clear();
            standalone.Reset();
            CloseActivity();
            return;
        }

        // QA A-7: ANTES habia aqui un `if (Credito is null) return;` que dejaba a
        // HioPos esperando para siempre: el POS espera un setResult, hacia
        // timeout y —segun el propio comentario de HioposConstants— "saca la
        // factura sin hacer nada". La rama Failed de BuildResponse existia pero
        // era codigo muerto, inalcanzable por ese early-return.
        //
        // Ahora SIEMPRE se responde, y todo va envuelto en try/catch como en
        // [ReciboPagoViewModel] (que si lo hacia bien): si armar la respuesta
        // falla, se devuelve Failed explicito en vez de colgar el POS.
        HioposResponse response;
        try
        {
            response = BuildResponse();
        }
        catch (Exception ex)
        {
            AppLogger.E("ConfirmacionViewModel",
                "Excepcion armando la respuesta del credito; se devuelve Failed al POS.", ex);
            response = BuildFailed(
                "No se pudo armar el comprobante del credito. " +
                (Credito is null
                    ? "Reintenta la venta."
                    : $"El credito {Credito.CreditNumber} SI fue creado: verificalo en Sistecredito."));
        }

        state.Clear();
        resultHandler.FinishWithResult(response);
    }

    private void CloseActivity()
    {
        try
        {
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.FinishAffinity();
        }
        catch (Exception ex)
        {
            AppLogger.E("ConfirmacionViewModel",
                "Error cerrando activity en modo standalone", ex);
        }
    }

    private HioposResponse BuildFailed(string message) =>
        new(HioposActions.Transaction,
            new Dictionary<string, string?>
            {
                [HioposExtras.TransactionResult] = TransactionResult.Failed.ToWire(),
                [HioposExtras.ErrorMessage] = message,
                [HioposExtras.ErrorMessageTitle] = "No se pudo completar el cobro"
            },
            ResultCode: Hiopos.Result.OkValue);

    private HioposResponse BuildResponse()
    {
        if (Credito is null)
            return BuildFailed("No hay credito creado para devolver al POS.");

        // La cedula va COMPLETA en el voucher impreso (el cliente firma su
        // credito); el enmascarado se usa para el campo CardNum que viaja al POS.
        var documento = state.ValidatedClient?.DocumentId ?? string.Empty;

        var merchantReceipt = receiptBuilder.BuildMerchantReceipt(
            fecha: DateTime.Now,
            creditNumber: CreditNumber,
            cliente: Cliente,
            documento: documento,
            valorFinanciado: Credito.CreditValue.ToColombianCurrency(),
            cuotaInicial: Credito.TotalDownPayment.ToColombianCurrency(),
            cuotaMensual: Credito.TotalFeeValue.ToColombianCurrency(),
            plazoCuotas: PlazoCuotas,
            tasaEfectivaAnual: Credito.EffectiveAnnualRate,
            primeraFechaPago: PrimeraFechaPago,
            tienda: Tienda,
            otpDestination: config.OtpDestination);

        // El voucher del cliente es identico al del comercio (ambos cortos, sin
        // datos fiscales) — se reutiliza en vez de reconstruirlo.
        var customerReceipt = merchantReceipt;

        var tx = state.ActiveTransaction;
        var authorizationId = DianFieldSanitizer.AuthorizationId(
            Credito.CreditId, Credito.CreditNumber);

        var amountCents = Money.ToCents(Credito.CreditValue);
        ValidateAmountAgainstIntent(amountCents, tx);

        // Sale de configuracion: ver [ICloudConfig.PaymentMeanIdVenta]. Antes era
        // una constante "2" repetida en dos ViewModels.
        var paymentMeanId = config.PaymentMeanIdVenta;

        var modifyResult = modifyDocBuilder.Build(
            paymentMeanId:    paymentMeanId,
            type:             "0",
            lineNumber:       "1",
            amount:           amountCents.ToString(CultureInfo.InvariantCulture),
            authorizationId:  authorizationId,
            transactionId:    null,
            customFields:
            [
                ("CreditId",      Credito.CreditId),
                ("CreditNumber",  Credito.CreditNumber.ToString(CultureInfo.InvariantCulture)),
                ("TEA",           Credito.EffectiveAnnualRate.ToString("N4", CultureInfo.InvariantCulture)),
                // "F0": sin separador de miles. La factura fiscal no acepta
                // separadores en campos numericos (ej. "34022", no "34,022").
                ("TotalFeeValue", Money.ToFiscalAmount(Credito.TotalFeeValue)),
                ("Fees",          PlazoCuotas.ToString(CultureInfo.InvariantCulture)),
                // QA M-5: "Frequency" es la PERIODICIDAD en dias (30), no el
                // numero de cuotas. Antes se enviaba state.Months bajo esta
                // etiqueta, o sea un dato con el significado equivocado.
                ("Frequency",     config.Frequency.ToString(CultureInfo.InvariantCulture))
            ]);

        return new HioposResponse(
            Action: HioposActions.Transaction,
            StringExtras: new Dictionary<string, string?>
            {
                [HioposExtras.TransactionResult] = TransactionResult.Accepted.ToWire(),
                [HioposExtras.TransactionType]   = tx?.TransactionType ?? "SALE",
                [HioposExtras.Amount]            = amountCents.ToString(CultureInfo.InvariantCulture),
                [HioposExtras.TipAmount]         = tx?.TipAmountCents ?? "0",
                [HioposExtras.TaxAmount]         = tx?.TaxAmountCents ?? "0",
                [HioposExtras.SurchargeAmount]   = tx?.SurchargeCents ?? "0",
                // Clave minimal (<=200 chars) para que el modulo fiscal la acepte
                // como numReferencia: con 3 campos el JSON llegaba a ~250 chars.
                [HioposExtras.TransactionData]   =
                    $"{{\"c\":\"{authorizationId}\",\"n\":{Credito.CreditNumber.ToString(CultureInfo.InvariantCulture)}}}",
                [HioposExtras.AuthorizationId]   = authorizationId,
                [HioposExtras.ModifyDocumentResult] = modifyResult,
                [HioposExtras.CardType]          = "Sistecredito",
                [HioposExtras.CardHolder]        = Cliente,
                [HioposExtras.CardNum]           = PiiMask.Document(state.ValidatedClient?.DocumentId)
            },
            ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// QA A-9: compara el monto que devolvemos al POS con el que HioPos pidio.
    ///
    /// El modulo reporta el valor REALMENTE financiado (coherente con el
    /// ModifyDocumentResult y con lo que ve la DIAN), no el Amount del Intent.
    /// Esa decision es deliberada, pero antes NO se verificaba que ambos
    /// coincidieran: si diferian, HioPos asentaba un pago por un monto distinto
    /// al de la venta y la factura quedaba descuadrada, en silencio.
    ///
    /// Ahora la discrepancia se registra en el log y en la auditoria para que sea
    /// rastreable y conciliable. No se aborta la venta: el credito ya existe en
    /// Credinet y cancelar dejaria al cliente con una deuda sin factura.
    /// </summary>
    private void ValidateAmountAgainstIntent(long amountCents, HioposTransaction? tx)
    {
        var requested = Money.FromCentsString(tx?.AmountCents);
        if (requested is null) return;

        var requestedCents = Money.ToCents(requested.Value);
        var diff = Math.Abs(requestedCents - amountCents);
        if (diff <= AmountToleranceCents) return;

        var message =
            $"DESCUADRE DE MONTO: HioPos pidio {requestedCents} centavos y el credito " +
            $"financio {amountCents} (diferencia {diff}). La factura del POS puede quedar " +
            "sin cuadrar; hay que conciliar esta venta.";

        AppLogger.E("ConfirmacionViewModel", message);
        audit.Log(AuditActions.CreditCreated, message,
            documentId: state.ActiveDocument?.SaleId ?? tx?.TransactionId);
    }
}
