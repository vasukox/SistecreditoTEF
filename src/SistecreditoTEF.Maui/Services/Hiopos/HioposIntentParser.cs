namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Parsea los extras de HioPosCloud en una representacion tipada
/// [HioposTransaction]. Libre de dependencias Android (testeable en xUnit).
///
/// [MainActivity] hace el bridge Android.OS.Bundle -> IReadOnlyDictionary
/// y delega aqui.
///
/// Doc §4: cubre los 16 extras de TRANSACTION. Los extras de INITIALIZE
/// (Parameters, Token, isReadOnly) se manejan en [MainActivity].
/// </summary>
public class HioposIntentParser
{
    public HioposTransaction Parse(IReadOnlyDictionary<string, string?> extras)
    {
        return new HioposTransaction(
            TransactionType:       GetString(extras, HioposExtras.TransactionType),
            TenderType:            GetString(extras, HioposExtras.TenderType),
            CurrencyIso:           GetString(extras, HioposExtras.CurrencyIso),
            LanguageIso:           GetString(extras, HioposExtras.LanguageIso),
            AmountCents:           GetString(extras, HioposExtras.Amount),
            TipAmountCents:        GetString(extras, HioposExtras.TipAmount),
            TaxAmountCents:        GetString(extras, HioposExtras.TaxAmount),
            TaxDetail:             GetString(extras, HioposExtras.TaxDetail),
            TransactionId:         GetString(extras, HioposExtras.TransactionId),
            TransactionData:       GetString(extras, HioposExtras.TransactionData),
            ReceiptPrinterColumns: GetString(extras, HioposExtras.ReceiptPrinterColumns),
            IsAdvancedPayment:     GetBool(extras, HioposExtras.IsAdvancedPayment),
            OverPaymentType:       GetInt(extras, HioposExtras.OverPaymentType),
            SurchargeCents:        GetString(extras, HioposExtras.SurchargeAmount),
            DocumentData:          GetString(extras, HioposExtras.DocumentData),
            DocumentPath:          GetString(extras, HioposExtras.DocumentPath),
            ShopData:              GetString(extras, HioposExtras.ShopData),
            SellerData:            GetString(extras, HioposExtras.SellerData));
    }

    private static string? GetString(
        IReadOnlyDictionary<string, string?> extras, string key)
    {
        if (!extras.TryGetValue(key, out var value)) return null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool GetBool(
        IReadOnlyDictionary<string, string?> extras, string key)
    {
        var raw = GetString(extras, key);
        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || raw == "1";
    }

    private static int GetInt(
        IReadOnlyDictionary<string, string?> extras, string key)
    {
        var raw = GetString(extras, key);
        return int.TryParse(raw, out var v) ? v : 0;
    }
}
