namespace SistecreditoTEF.Maui.Models;

/// <summary>
/// Modelo de negocio: Pago aplicado a un credito.
/// En Kotlin era @Parcelize; aqui usamos record (inmutable, ya serializable
/// para pasar entre Pages via Shell navigation params).
/// </summary>
public record Payment(
    string TypeDocument,
    string IdDocument,
    string CreditId,
    string PaymentId,
    int PaymentNumber,
    double CreditValuePaid,
    double InterestValuePaid,
    double ArrearsValuePaid,
    double AssuranceValuePaid,
    double ChargeValuePaid,
    double Balance,
    string NextDueDate,
    double NextMinimumPayment);
