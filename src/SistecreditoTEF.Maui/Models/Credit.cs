namespace SistecreditoTEF.Maui.Models;

/// <summary>
/// Modelo de negocio: Credito recien creado (respuesta de POST /create).
/// </summary>
public record Credit(
    string TypeDocument,
    string IdDocument,
    string CreditId,
    int CreditNumber,
    double EffectiveAnnualRate,
    double DownPayment,
    double TotalFeeValue,
    double CreditValue,
    int Fees,
    double AssuranceValue,
    double InterestRate,
    double TotalInterestValue,
    double TotalDownPayment,
    double FeeCreditValue,
    double AssuranceFeeValue,
    double AssuranceTotalValue,
    double AssuranceTaxFeeValue);
