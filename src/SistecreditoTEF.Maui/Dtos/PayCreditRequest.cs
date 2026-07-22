using System.Text.Json.Serialization;

namespace SistecreditoTEF.Maui.Dtos;

/// <summary>
/// Body de POST /pos/payCredit.
///
/// Campos obligatorios: creditId, totalValuePaid, userName.
/// Opcionales: storeId.
/// </summary>
public record PayCreditRequest(
    [property: JsonPropertyName("creditId")] string CreditId,
    [property: JsonPropertyName("totalValuePaid")] double TotalValuePaid,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("storeId")] string? StoreId = null);
