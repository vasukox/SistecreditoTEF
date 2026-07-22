using System.Text.Json.Serialization;

namespace SistecreditoTEF.Maui.Dtos;

/// <summary>
/// Respuesta de POST /pos/payCredit.
/// </summary>
public record PaymentDto(
    [property: JsonPropertyName("typeDocument")] string? TypeDocument,
    [property: JsonPropertyName("idDocument")] string? IdDocument,
    [property: JsonPropertyName("creditId")] string? CreditId,
    [property: JsonPropertyName("paymentId")] string? PaymentId,
    [property: JsonPropertyName("paymentNumber")] int? PaymentNumber,
    [property: JsonPropertyName("creditValuePaid")] double? CreditValuePaid,
    [property: JsonPropertyName("interestValuePaid")] double? InterestValuePaid,
    [property: JsonPropertyName("arrearsValuePaid")] double? ArrearsValuePaid,
    [property: JsonPropertyName("assuranceValuePaid")] double? AssuranceValuePaid,
    [property: JsonPropertyName("chargeValuePaid")] double? ChargeValuePaid,
    [property: JsonPropertyName("balance")] double? Balance,
    [property: JsonPropertyName("nextDueDate")] string? NextDueDate,
    [property: JsonPropertyName("nextMinimumPayment")] double? NextMinimumPayment);
