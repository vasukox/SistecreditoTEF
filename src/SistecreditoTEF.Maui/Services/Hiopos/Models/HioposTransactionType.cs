namespace SistecreditoTEF.Maui.Services.Hiopos.Models;

/// <summary>
/// Tipos de transaccion que HioPosCloud envia en el Intent de TRANSACTION.
/// Spec ICG doc §4.
/// </summary>
public enum HioposTransactionType
{
    Unknown,
    Sale,
    Refund,
    NegativeSale,
    AdjustTips,
    VoidTransaction,
    QueryTransaction,
    BatchClose
}

public static class HioposTransactionTypeExtensions
{
    public static HioposTransactionType FromString(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out HioposTransactionType r)
            ? r
            : HioposTransactionType.Unknown;
}
