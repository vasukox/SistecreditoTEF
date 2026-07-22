using System.Text.Json.Serialization;

namespace SistecreditoTEF.Maui.Dtos;

/// <summary>
/// Respuesta compartida de /getCreditDetails y /getSimulatedCreditDetails.
/// Las dos endpoints devuelven el mismo shape (V5 - DRY).
/// </summary>
public record CreditDetailsDto(
    [property: JsonPropertyName("downPayment")]               double? DownPayment,
    [property: JsonPropertyName("totalFeeValue")]             double? TotalFeeValue,
    [property: JsonPropertyName("creditValue")]               double? CreditValue,
    [property: JsonPropertyName("fees")]                      int?    Fees,
    [property: JsonPropertyName("assuranceValue")]            double? AssuranceValue,
    [property: JsonPropertyName("interestRate")]              double? InterestRate,
    [property: JsonPropertyName("totalInterestValue")]        double? TotalInterestValue,
    [property: JsonPropertyName("totalDownPayment")]          double? TotalDownPayment,
    [property: JsonPropertyName("feeCreditValue")]            double? FeeCreditValue,
    [property: JsonPropertyName("assuranceFeeValue")]         double? AssuranceFeeValue,
    [property: JsonPropertyName("assuranceTotalValue")]        double? AssuranceTotalValue,
    [property: JsonPropertyName("assuranceTaxFeeValue")]      double? AssuranceTaxFeeValue,
    [property: JsonPropertyName("assuranceTaxValue")]          double? AssuranceTaxValue,
    [property: JsonPropertyName("downPaymentPercentage")]     double? DownPaymentPercentage,
    [property: JsonPropertyName("assurancePercentage")]       double? AssurancePercentage,
    [property: JsonPropertyName("assuranceTotalFeeValue")]    double? AssuranceTotalFeeValue,
    [property: JsonPropertyName("totalPaymentValue")]         double? TotalPaymentValue,
    [property: JsonPropertyName("customerAllowPhotoSignature")] bool? CustomerAllowPhotoSignature);
