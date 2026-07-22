using System.Text.Json.Serialization;

namespace SistecreditoTEF.Maui.Dtos;

/// <summary>
/// Respuesta de POST /pos/create. El campo `data` de la ApiResponse es esto.
/// </summary>
public record CreditDto(
    [property: JsonPropertyName("typeDocument")] string? TypeDocument,
    [property: JsonPropertyName("idDocument")] string? IdDocument,
    [property: JsonPropertyName("creditId")] string? CreditId,
    [property: JsonPropertyName("creditNumber")] int? CreditNumber,
    [property: JsonPropertyName("effectiveAnnualRate")] double? EffectiveAnnualRate,
    [property: JsonPropertyName("downPayment")] double? DownPayment,
    [property: JsonPropertyName("totalFeeValue")] double? TotalFeeValue,
    [property: JsonPropertyName("creditValue")] double? CreditValue,
    [property: JsonPropertyName("fees")] int? Fees,
    [property: JsonPropertyName("assuranceValue")] double? AssuranceValue,
    [property: JsonPropertyName("interestRate")] double? InterestRate,
    [property: JsonPropertyName("totalInterestValue")] double? TotalInterestValue,
    [property: JsonPropertyName("totalDownPayment")] double? TotalDownPayment,
    [property: JsonPropertyName("feeCreditValue")] double? FeeCreditValue,
    [property: JsonPropertyName("assuranceFeeValue")] double? AssuranceFeeValue,
    [property: JsonPropertyName("assuranceTotalValue")] double? AssuranceTotalValue,
    [property: JsonPropertyName("assuranceTaxFeeValue")] double? AssuranceTaxFeeValue);
