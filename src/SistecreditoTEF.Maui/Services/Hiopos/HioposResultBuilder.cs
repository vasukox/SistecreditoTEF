using SistecreditoTEF.Maui.Services.Hiopos.Models;

namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Construye el POCO [HioposResponse] que [MainActivity] luego
/// materializa en [Android.Content.Intent].
///
/// Libre de dependencias Android (V8): se testea en xUnit puro.
///
/// Doc §2-§4: cubre las 11 actions que HioPosCloud puede disparar.
/// Para cada una hay un metodo dedicado con la forma exacta del
/// response esperada por el POS.
/// </summary>
public class HioposResultBuilder
{
    private readonly ReceiptBuilder _receiptBuilder;

    public HioposResultBuilder(ReceiptBuilder receiptBuilder)
    {
        _receiptBuilder = receiptBuilder;
    }

    // ------------------------------------------------------------------
    // ACTIONS SINCRONAS (responder rapido + Finish)
    // ------------------------------------------------------------------

    /// <summary>Responde a INITIALIZE / FINALIZE con resultado OK vacio.</summary>
    public HioposResponse BuildOk(string action) =>
        new(action, new Dictionary<string, string?>());

    /// <summary>Responde a GET_VERSION con la version del assembly.</summary>
    public HioposResponse BuildVersion(string action, string version)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.Version] = version
        };
        return new HioposResponse(action, extras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// Responde a GET_BEHAVIOR con las 18 capacidades (doc §3).
    /// Includes [OnlyUseDocumentPath=true] que es critico para
    /// facturas grandes (doc §3 y §12 gotcha #2).
    /// </summary>
    public HioposResponse BuildBehavior(string action)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.SupportsTransactionVoid]    = HioposCapabilities.SupportsTransactionVoid    ? "true" : "false",
            [HioposExtras.SupportsTransactionQuery]   = HioposCapabilities.SupportsTransactionQuery   ? "true" : "false",
            [HioposExtras.SupportsNegativeSales]      = HioposCapabilities.SupportsNegativeSales      ? "true" : "false",
            [HioposExtras.SupportsPartialRefund]      = HioposCapabilities.SupportsPartialRefund      ? "true" : "false",
            [HioposExtras.SupportsBatchClose]         = HioposCapabilities.SupportsBatchClose         ? "true" : "false",
            [HioposExtras.SupportsTipAdjustment]      = HioposCapabilities.SupportsTipAdjustment      ? "true" : "false",
            [HioposExtras.SupportsCredit]             = HioposCapabilities.SupportsCredit             ? "true" : "false",
            [HioposExtras.SupportsDebit]              = HioposCapabilities.SupportsDebit              ? "true" : "false",
            [HioposExtras.SupportsEBTFoodstamp]       = HioposCapabilities.SupportsEBTFoodstamp       ? "true" : "false",
            [HioposExtras.HasCustomParams]            = HioposCapabilities.HasCustomParams            ? "true" : "false",
            [HioposExtras.CanChargeCard]              = HioposCapabilities.CanChargeCard              ? "true" : "false",
            [HioposExtras.CanAudit]                   = HioposCapabilities.CanAudit                   ? "true" : "false",
            [HioposExtras.ExecuteVoidWhenAvailable]   = HioposCapabilities.ExecuteVoidWhenAvailable   ? "true" : "false",
            [HioposExtras.SaveLoyaltyCardNum]         = HioposCapabilities.SaveLoyaltyCardNum         ? "true" : "false",
            [HioposExtras.CanPrint]                   = HioposCapabilities.CanPrint                   ? "true" : "false",
            [HioposExtras.ReadCardFromApi]            = HioposCapabilities.ReadCardFromApi            ? "true" : "false",
            [HioposExtras.OnlyUseDocumentPath]        = HioposCapabilities.OnlyUseDocumentPath        ? "true" : "false"
        };
        return new HioposResponse(action, extras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// Responde a GET_CUSTOM_PARAMS (doc §9) con el logo y nombre del modulo.
    /// Sin esto, el cajero ve un logo generico en la pantalla de medios de pago.
    /// </summary>
    public HioposResponse BuildCustomParams(string action, byte[] logoPng)
    {
        var stringExtras = new Dictionary<string, string?>
        {
            [HioposExtras.Name] = "Sistecredito"
        };
        var binaryExtras = new Dictionary<string, byte[]>
        {
            [HioposExtras.Logo] = logoPng
        };
        return new HioposResponse(action, stringExtras, binaryExtras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// Responde a GET_PRINT_INFO (doc §10.2). Aunque en MVP
    /// [HioposCapabilities.CanPrint=false], se implementa igual por si
    /// en el futuro se activa.
    /// </summary>
    public HioposResponse BuildPrintInfo(string action, string packageName, int horizontalDots)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.PackageName]    = packageName,
            [HioposExtras.HorizontalDots] = horizontalDots.ToString()
        };
        return new HioposResponse(action, extras, ResultCode: Hiopos.Result.OkValue);
    }

    // ------------------------------------------------------------------
    // TRANSACTION: ACCEPTED / FAILED
    // ------------------------------------------------------------------

    /// <summary>
    /// Responde a TRANSACTION con ACCEPTED + comprobantes + datos
    /// del credito creado. Doc §4 y §5.
    /// </summary>
    public HioposResponse BuildTransactionAccepted(
        string merchantReceiptXml,
        string customerReceiptXml,
        string authorizationId,
        string? cardHolder = null,
        string? cardNum = null)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.TransactionResult] = TransactionResult.Accepted.ToWire(),
            [HioposExtras.MerchantReceipt]   = merchantReceiptXml,
            [HioposExtras.CustomerReceipt]   = customerReceiptXml,
            [HioposExtras.AuthorizationId]    = authorizationId,
            [HioposExtras.CardType]          = "Sistecredito",
            [HioposExtras.CardHolder]        = cardHolder,
            [HioposExtras.CardNum]           = cardNum
        };
        return new HioposResponse(HioposActions.Transaction, extras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// Responde a TRANSACTION con FAILED (doc §4). Solo expone
    /// ErrorMessage y ErrorMessageTitle - el error code interno se
    /// loguea aparte, no viaja al POS (B5).
    /// </summary>
    public HioposResponse BuildTransactionFailed(string errorMessage, string? errorTitle = null)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.TransactionResult]    = TransactionResult.Failed.ToWire(),
            [HioposExtras.ErrorMessage]         = errorMessage,
            [HioposExtras.ErrorMessageTitle]    = errorTitle ?? "No se pudo completar el cobro"
        };
        return new HioposResponse(HioposActions.Transaction, extras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// Responde a una action no soportada (READ_CARD / CHARGE_CARD /
    /// GET_CARD_DATA en Sistecredito) con RESULT_CANCELED (B6).
    /// </summary>
    public HioposResponse BuildCanceled(string action) =>
        new(action, new Dictionary<string, string?>(), ResultCode: Hiopos.Result.CanceledValue);
}
