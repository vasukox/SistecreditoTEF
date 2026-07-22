namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// DTO inmutable con TODOS los campos que HioPosCloud puede enviar en
/// el Intent de TRANSACTION (doc §4 + §6).
///
/// Strings en CENTAVOS para Amount/TipAmount/TaxAmount/Surcharge
/// (ej "50000000" = $500.000 COP). Conversion a pesos via
/// [MoneyConverter.FromCentsToPesos].
///
/// KISS (V6): ya NO expone [AmountInPesos] - usar
/// [MoneyConverter.FromCentsToPesos(transaction.Amount)] directamente.
/// </summary>
public sealed record HioposTransaction(
    string? TransactionType,
    string? TenderType,
    string? CurrencyIso,
    string? LanguageIso,
    string? AmountCents,
    string? TipAmountCents,
    string? TaxAmountCents,
    string? TaxDetail,
    string? TransactionId,
    string? TransactionData,
    string? ReceiptPrinterColumns,
    bool    IsAdvancedPayment,
    int     OverPaymentType,
    string? SurchargeCents,
    string? DocumentData,
    string? DocumentPath,
    string? ShopData,
    string? SellerData)
{
    public bool IsSale => string.Equals(TransactionType, "SALE", StringComparison.OrdinalIgnoreCase);
    public bool IsRefund => string.Equals(TransactionType, "REFUND", StringComparison.OrdinalIgnoreCase);
}
