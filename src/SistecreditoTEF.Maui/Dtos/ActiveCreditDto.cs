using System.Text.Json.Serialization;

namespace SistecreditoTEF.Maui.Dtos;

/// <summary>
/// Respuesta de GET /pos/getactivecredits. El campo `data` es ARRAY de esto.
/// </summary>
public record ActiveCreditDto(
    [property: JsonPropertyName("typeDocument")] string? TypeDocument,
    [property: JsonPropertyName("idDocument")] string? IdDocument,
    [property: JsonPropertyName("creditId")] string? CreditId,
    [property: JsonPropertyName("creditNumber")] int? CreditNumber,
    [property: JsonPropertyName("createDate")] string? CreateDate,
    [property: JsonPropertyName("creditValue")] double? CreditValue,
    [property: JsonPropertyName("arrearsDays")] int? ArrearsDays,
    [property: JsonPropertyName("minimumPayment")] double? MinimumPayment,
    [property: JsonPropertyName("totalPayment")] double? TotalPayment,
    [property: JsonPropertyName("feeValue")] double? FeeValue,
    [property: JsonPropertyName("storeName")] string? StoreName,
    [property: JsonPropertyName("balance")] double? Balance,
    [property: JsonPropertyName("dueDate")] string? DueDate);
