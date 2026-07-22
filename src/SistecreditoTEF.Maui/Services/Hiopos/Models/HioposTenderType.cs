namespace SistecreditoTEF.Maui.Services.Hiopos.Models;

/// <summary>
/// Tipos de medio de pago del Intent de TRANSACTION.
/// Spec ICG doc §4: CREDIT, DEBIT, EBT_FOODSTAMP.
/// MVP: solo CREDIT (Sistecredito es un medio de credito).
/// </summary>
public enum HioposTenderType
{
    Unknown,
    Credit,
    Debit,
    EbtFoodstamp
}

public static class HioposTenderTypeExtensions
{
    public static HioposTenderType FromString(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out HioposTenderType r)
            ? r
            : HioposTenderType.Unknown;
}
