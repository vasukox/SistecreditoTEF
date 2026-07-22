using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Hiopos.Models;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 8: recibo del pago + devolucion al POS.
/// B10: usa [ITransactionResultHandler] en lugar de Quit().
/// </summary>
public partial class ReciboPagoViewModel(
    ITransactionStateStore state,
    ITransactionResultHandler resultHandler,
    ReceiptBuilder receiptBuilder,
    ModifyDocumentResultBuilder modifyDocBuilder) : ObservableObject
{
    [ObservableProperty]
    private Payment? pago;

    public string NumeroPago     => Pago is null ? "-" : $"#{Pago.PaymentNumber}";
    public string CapitalPagado  => Pago?.CreditValuePaid.ToColombianCurrency() ?? "$ 0";
    public string SaldoRestante  => Pago?.Balance.ToColombianCurrency() ?? "$ 0";
    public string ProximoPago    => Pago?.NextDueDate ?? "-";
    public string ProximoMinimo  => Pago?.NextMinimumPayment.ToColombianCurrency() ?? "$ 0";
    public string FechaPago      => DateTime.Now.ToString("dd/MM/yyyy HH:mm");

    partial void OnPagoChanged(Payment? value)
    {
        OnPropertyChanged(nameof(NumeroPago));
        OnPropertyChanged(nameof(CapitalPagado));
        OnPropertyChanged(nameof(SaldoRestante));
        OnPropertyChanged(nameof(ProximoPago));
        OnPropertyChanged(nameof(ProximoMinimo));
    }

    public void Inicializar()
    {
        Pago = state.LastPayment;
    }

    [RelayCommand]
    private void Finalizar()
    {
        if (Pago is null)
        {
            resultHandler.FinishWithResult(
                new HioposResponse(HioposActions.Transaction,
                    new Dictionary<string, string?>
                    {
                        [HioposExtras.TransactionResult] = TransactionResult.Failed.ToWire(),
                        [HioposExtras.ErrorMessage] = "No hay pago para devolver al POS."
                    },
                    ResultCode: Hiopos.Result.OkValue));
            return;
        }

        try
        {
            var merchantReceipt = receiptBuilder.BuildPaymentReceipt(
                tienda: "Permoda",
                fecha: DateTime.Now,
                cajero: "Cajero Permoda",
                paymentNumber: Pago.PaymentNumber.ToString(),
                // #7: nº de credito LEGIBLE (el int), no el GUID. Fallback al
                // CreditId si por alguna razon no hay SelectedCredit.
                creditNumber: state.SelectedCredit?.CreditNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Pago.CreditId,
                capitalPagado: Pago.CreditValuePaid,
                saldoRestante: Pago.Balance,
                proximoPago: Pago.NextDueDate,
                proximoMinimo: Pago.NextMinimumPayment,
                cliente: state.ValidatedClient?.FullName ?? string.Empty);

            // HU8-973: echo-back completo tambien en el flujo de PAGO (antes solo
            // estaba en Confirmacion). El monto se toma del Intent original si vino,
            // si no se calcula del total efectivamente pagado.
            // Auditoria dinero (C1): el monto devuelto al POS DEBE ser lo
            // REALMENTE pagado, no el Amount del Intent (que es el total de la
            // factura HioPos). Antes usaba tx?.AmountCents -> el POS podia
            // asentar un recaudo distinto al cobrado. Un abono es
            // self-contained: no hereda tip/tax/tipo de la venta.
            var paidTotal = Pago.CreditValuePaid + Pago.InterestValuePaid + Pago.ArrearsValuePaid
                          + Pago.AssuranceValuePaid + Pago.ChargeValuePaid;
            var amountCents = ToCents(paidTotal);

            // HU-DIAN: clave minimal (<=200 chars) para el modulo fiscal,
            // mismo patron que ConfirmacionViewModel. El JSON grande con 3
            // campos (paymentId+creditId+saleId) puede llegar a ~250 chars y
            // supera el cap interno de 200 del modulo icg.hioposapifiscal.
            var sanitizedPaymentId = SanitizeForDian(Pago.PaymentId, Pago.PaymentNumber);

            // HU-DIAN: el modulo fiscal necesita el ModifyDocumentResult para
            // identificar el medio de pago Sistecredito en la factura del POS.
            // Sin esto, el modulo no puede conciliar el pago con la factura
            // (especialmente cuando el cajero la dejo en espera y vos entraste
            // directo al APK). PaymentMeanId=2 hardcoded = "Tarjeta" del CloudLicense.
            var modifyResult = modifyDocBuilder.Build(
                paymentMeanId:    "2",
                type:             "0",
                lineNumber:       "1",
                amount:           amountCents,
                authorizationId:  sanitizedPaymentId,
                transactionId:    null,
                customFields: new (string, string)[] {
                    ("PaymentId",     Pago.PaymentId),
                    ("CreditId",      Pago.CreditId),
                    ("CapitalPaid",   Pago.CreditValuePaid.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)),
                    ("InterestPaid",  Pago.InterestValuePaid.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)),
                    ("ArrearsPaid",   Pago.ArrearsValuePaid.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)),
                    ("Balance",       Pago.Balance.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))
                });

            var response = new HioposResponse(
                Action: HioposActions.Transaction,
                StringExtras: new Dictionary<string, string?>
                {
                    [HioposExtras.TransactionResult] = TransactionResult.Accepted.ToWire(),
                    [HioposExtras.TransactionType]   = "SALE",
                    [HioposExtras.Amount]            = amountCents,
                    [HioposExtras.TipAmount]         = "0",
                    [HioposExtras.TaxAmount]         = "0",
                    [HioposExtras.SurchargeAmount]   = "0",
                    [HioposExtras.TransactionData]   = "{\"p\":\"" + sanitizedPaymentId + "\",\"n\":" +
                        Pago.PaymentNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}",
                    [HioposExtras.ModifyDocumentResult] = modifyResult,
                    [HioposExtras.MerchantReceipt]   = merchantReceipt,
                    [HioposExtras.CustomerReceipt]   = merchantReceipt,
                    [HioposExtras.AuthorizationId]   = sanitizedPaymentId,
                    [HioposExtras.CardType]          = "Sistecredito",
                    [HioposExtras.CardHolder]        = state.ValidatedClient?.FullName
                },
                ResultCode: Hiopos.Result.OkValue);

            state.Clear();
            resultHandler.FinishWithResult(response);
        }
        catch (Exception ex)
        {
            // HU8-973 BugFix #7: si la construccion del recibo o el armado del
            // HioposResponse falla (campos null, conversion, etc.), NO dejamos
            // a HI-POS esperando para siempre. Devolvemos un Failed explicito.
            AppLogger.E("ReciboPagoViewModel",
                "Excepcion inesperada armando respuesta del pago", ex);

            resultHandler.FinishWithResult(
                new HioposResponse(
                    HioposActions.Transaction,
                    new Dictionary<string, string?>
                    {
                        [HioposExtras.TransactionResult] = TransactionResult.Failed.ToWire(),
                        [HioposExtras.ErrorMessage]      = $"Error armando recibo: {ex.Message}"
                    },
                    ResultCode: Hiopos.Result.OkValue));
        }
    }

    private static string ToCents(double pesos) =>
        ((long)Math.Round(pesos * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // HU-DIAN: mismo sanitizer que ConfirmacionViewModel. Quita guiones y
    // espacios, trunca a 40 chars, fallback a paymentNumber padded si vacio.
    private const int AuthorizationIdMaxLength = 40;
    private static string SanitizeForDian(string? paymentId, int paymentNumber)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
            return paymentNumber.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var s = paymentId.Replace("-", string.Empty, StringComparison.Ordinal)
                         .Replace(" ", string.Empty, StringComparison.Ordinal)
                         .Trim();
        if (s.Length == 0)
            return paymentNumber.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        return s.Length > AuthorizationIdMaxLength ? s[..AuthorizationIdMaxLength] : s;
    }
}
