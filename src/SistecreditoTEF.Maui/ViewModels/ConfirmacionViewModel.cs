using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Hiopos.Models;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 5: confirmacion del credito creado + devolucion al POS.
///
/// Finalizar entrega el [HioposResponse] al POS via
/// [ITransactionResultHandler] (B1) y cierra la Activity.
/// </summary>
public partial class ConfirmacionViewModel(
    ITransactionStateStore state,
    ITransactionResultHandler resultHandler,
    ReceiptBuilder receiptBuilder,
    ModifyDocumentResultBuilder modifyDocBuilder) : ObservableObject
{
    [ObservableProperty]
    private Credit? credito;

    public string CreditId => Credito?.CreditId ?? string.Empty;
    public string CreditNumber => Credito?.CreditNumber.ToString() ?? "0";
    public string MontoAprobado => Credito?.CreditValue.ToColombianCurrency() ?? "$ 0";
    public string Meses => $"{state.Months} cuotas mensuales";
    public string PrimeraFechaPago => DateHelper.CalcularPrimeraFechaEstimadaPago();
    public string Tienda => "Permoda";
    public string Cajero => state.ActiveTransaction is null
        ? "Cajero Permoda"
        : ExtractSellerName(state.ActiveTransaction.SellerData);
    public string Cliente => state.ValidatedClient?.FullName ?? string.Empty;

    public bool IsSuccess => Credito is not null;

    partial void OnCreditoChanged(Credit? value)
    {
        OnPropertyChanged(nameof(CreditId));
        OnPropertyChanged(nameof(CreditNumber));
        OnPropertyChanged(nameof(MontoAprobado));
        OnPropertyChanged(nameof(IsSuccess));
    }

    public void Inicializar()
    {
        Credito = state.CreatedCredit;
    }

    [RelayCommand]
    private void Finalizar()
    {
        if (Credito is null) return;

        var response = BuildResponse();

        // Entregamos el Intent via SetResult y cerramos la Activity.
        state.Clear();
        resultHandler.FinishWithResult(response);
    }

    private HioposResponse BuildResponse()
    {
        if (Credito is null)
        {
            return new HioposResponse(HioposActions.Transaction,
                new Dictionary<string, string?>
                {
                    [HioposExtras.TransactionResult] = TransactionResult.Failed.ToWire(),
                    [HioposExtras.ErrorMessage] = "No hay credito creado para devolver al POS."
                },
                ResultCode: Hiopos.Result.OkValue);
        }

        // HU-134: el comprobante es un VOUCHER de credito (no una factura).
        // La cedula va COMPLETA en el voucher impreso; el enmascarado se usa
        // solo para el campo CardNum que viaja al POS.
        var documento = state.ValidatedClient?.DocumentId ?? string.Empty;

        // Log del valor CRUDO de la TEA: la API la envia como fraccion
        // (ej. 0.2832) y el ReceiptBuilder la normaliza a "28.32%". Si un dia
        // la API cambiara a porcentaje, este log lo delataria.
        AppLogger.I("ConfirmacionViewModel",
            $"TEA cruda de la API = {Credito.EffectiveAnnualRate} -> se normaliza al imprimir.");

        var merchantReceipt = receiptBuilder.BuildMerchantReceipt(
            fecha: DateTime.Now,
            creditNumber: Credito.CreditNumber.ToString(),
            cliente: Cliente,
            documento: documento,
            valorFinanciado: Credito.CreditValue.ToColombianCurrency(),
            cuotaInicial: Credito.TotalDownPayment.ToColombianCurrency(),
            cuotaMensual: Credito.TotalFeeValue.ToColombianCurrency(),
            plazoCuotas: state.Months,
            tasaEfectivaAnual: Credito.EffectiveAnnualRate,
            primeraFechaPago: PrimeraFechaPago,
            tienda: Tienda);

        var customerReceipt = receiptBuilder.BuildCustomerReceipt(
            fecha: DateTime.Now,
            creditNumber: Credito.CreditNumber.ToString(),
            cliente: Cliente,
            documento: documento,
            valorFinanciado: Credito.CreditValue.ToColombianCurrency(),
            cuotaInicial: Credito.TotalDownPayment.ToColombianCurrency(),
            cuotaMensual: Credito.TotalFeeValue.ToColombianCurrency(),
            plazoCuotas: state.Months,
            tasaEfectivaAnual: Credito.EffectiveAnnualRate,
            primeraFechaPago: PrimeraFechaPago,
            tienda: Tienda);

        // HU-DIAN: el modulo fiscal de ICG (icg.hioposapifiscal) rechaza el
        // "numero de referencia del pago" si HioPosCloud le pasa un
        // TransactionData >200 chars (su cap interno, mas estricto que el
        // limite oficial de 250 de HioPos). Causa actual: con 3 campos
        // (creditId GUID + creditNumber + saleId UUID) el JSON serializado
        // media ~250 chars -> rechazado.
        // FIX: usar solo los IDs abreviados ("c" = creditId, "n" =
        // creditNumber), ~70 chars. HioPosCloud acepta cualquier JSON valido;
        // el POS hace la conciliacion por AuthorizationId + CardType + el
        // ModifyDocumentResult de abajo (que SI viaja con todos los campos
        // enriquecidos para el modulo fiscal).
        var tx = state.ActiveTransaction;
        var saleId = state.ActiveDocument?.SaleId ?? tx?.TransactionId ?? string.Empty;
        var sanitizedAuthId = SanitizeForDian(Credito.CreditId, Credito.CreditNumber);

        // ModifyDocumentResult (HU-DIAN): enriquece el medio de pago de la
        // factura con los datos del credito para la DIAN. Solo PaymentMeans,
        // SIN lineas de producto ni totales (si parece otro documento, el
        // fiscal lo reenvia a DIAN).
        //
        // PaymentMeanId HARDCODEADO a "2" = medio "Tarjeta" del CloudLicense:
        // Sistecredito se consolida sobre ese medio. NO se lee del documento
        // porque al iniciar la transaccion PaymentMeans viene vacio (el medio
        // se agrega despues de que respondemos); un Id vacio hacia que el
        // modulo fiscal reportara "error en consulta de folios".
        // TODO: mover a CloudConfigStore si ICG asigna un Id propio a Sistecredito.
        const string paymentMeanId = "2";

        var modifyResult = modifyDocBuilder.Build(
            paymentMeanId:    paymentMeanId,
            type:             "0",
            lineNumber:       "1",
            amount:           ToCents(Credito.CreditValue),
            authorizationId:  sanitizedAuthId,
            transactionId:    null,
            customFields: new (string, string)[] {
                ("CreditId",      Credito.CreditId),
                ("CreditNumber",  Credito.CreditNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("TEA",           Credito.EffectiveAnnualRate.ToString("N4", System.Globalization.CultureInfo.InvariantCulture)),
                // "F0" (no "N0"): sin separador de miles. La factura fiscal no
                // acepta separadores en campos numericos (ej. "34022", no "34,022").
                ("TotalFeeValue", Credito.TotalFeeValue.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)),
                ("Fees",          Credito.Fees.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Frequency",     state.Months.ToString(System.Globalization.CultureInfo.InvariantCulture))
            });
        AppLogger.I("ConfirmacionViewModel",
            $"ModifyDocumentResult incluido (PaymentMeanId={paymentMeanId}).");

        return new HioposResponse(
            Action: HioposActions.Transaction,
            StringExtras: new Dictionary<string, string?>
            {
                [HioposExtras.TransactionResult] = TransactionResult.Accepted.ToWire(),
                [HioposExtras.TransactionType]   = tx?.TransactionType ?? "SALE",
                // Auditoria dinero (A1): reportar el valor REALMENTE financiado
                // (coherente con el ModifyDocumentResult), no el Amount del
                // Intent. Si coinciden (monto no editado) da lo mismo; si el
                // cajero edito el monto, evita que POS y DIAN difieran.
                [HioposExtras.Amount]            = ToCents(Credito.CreditValue),
                [HioposExtras.TipAmount]         = tx?.TipAmountCents ?? "0",
                [HioposExtras.TaxAmount]         = tx?.TaxAmountCents ?? "0",
                [HioposExtras.SurchargeAmount]   = tx?.SurchargeCents ?? "0",
                // HU-DIAN: clave minimal (<=200 chars para que el fiscal
                // la acepte como numReferencia). Llaves abreviadas.
                [HioposExtras.TransactionData]   = "{\"c\":\"" + sanitizedAuthId + "\",\"n\":" +
                    Credito.CreditNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}",
                // HU-134: NO imprimimos voucher de credito adicional.
                // HU-DIAN: AuthorizationId sanitizado (sin guiones, <=200
                // chars, fallback a creditNumber padded si vacio).
                [HioposExtras.AuthorizationId]   = sanitizedAuthId,
                // HU-DIAN: enriquecimiento del PaymentMean para el modulo
                // fiscal (DIAN) sin agregar lineas de producto.
                [HioposExtras.ModifyDocumentResult] = modifyResult,
                [HioposExtras.CardType]          = "Sistecredito",
                [HioposExtras.CardHolder]        = Cliente,
                [HioposExtras.CardNum]           = MaskDocumentId(state.ValidatedClient?.DocumentId)
            },
            ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// HU-DIAN: el modulo fiscal (icg.hioposapifiscal) usa este campo como
    /// "numero de referencia del pago" y lo mapea a AuthorizationId, que el
    /// manual HioPos define como <b>varchar(40)</b>; ademas la API SIAT/DIAN
    /// rechaza guiones, espacios o vacios. Devuelve el creditId alfanumerico
    /// sin guiones (un GUID sin guiones = 32 chars), truncado a 40, o el
    /// creditNumber padded a 6 digitos si viene vacio.
    /// </summary>
    private const int AuthorizationIdMaxLength = 40;

    private static string SanitizeForDian(string? creditId, int creditNumber)
    {
        if (string.IsNullOrWhiteSpace(creditId))
            return creditNumber.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var s = creditId.Replace("-", string.Empty, StringComparison.Ordinal)
                        .Replace(" ", string.Empty, StringComparison.Ordinal)
                        .Trim();
        if (s.Length == 0)
            return creditNumber.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        return s.Length > AuthorizationIdMaxLength ? s[..AuthorizationIdMaxLength] : s;
    }

    private static string ToCents(double pesos) =>
        ((long)Math.Round(pesos * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string MaskDocumentId(string? docId)
    {
        if (string.IsNullOrEmpty(docId) || docId.Length < 4)
            return docId ?? string.Empty;
        return new string('*', docId.Length - 4) + docId[^4..];
    }

    private static string ExtractSellerName(string? sellerData)
    {
        if (string.IsNullOrWhiteSpace(sellerData)) return "Cajero Permoda";
        var match = System.Text.RegularExpressions.Regex.Match(
            sellerData, @"<name>([^<]+)</name>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "Cajero Permoda";
    }
}
