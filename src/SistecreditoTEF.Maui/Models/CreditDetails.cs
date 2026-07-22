namespace SistecreditoTEF.Maui.Models;

/// <summary>
/// Modelo de negocio compartido: detalle de credito (cuota) calculado
/// por CREDINET en /getCreditDetails o /getSimulatedCreditDetails.
/// </summary>
public sealed record CreditDetails(
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
    double AssuranceTaxFeeValue,
    double AssuranceTaxValue,
    double DownPaymentPercentage,
    double AssurancePercentage,
    double AssuranceTotalFeeValue,
    double TotalPaymentValue,
    bool CustomerAllowPhotoSignature);
