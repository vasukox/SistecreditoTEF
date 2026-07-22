using System.Text.Json.Serialization;

namespace SistecreditoTEF.Maui.Dtos;

/// <summary>
/// Datos del cliente que devuelve CREDINET al validar cupo.
/// Nullable por si el backend omite campos.
/// </summary>
public record ClientDto(
    [property: JsonPropertyName("typeDocument")] string? TypeDocument,
    [property: JsonPropertyName("idDocument")] string? IdDocument,
    [property: JsonPropertyName("creditLimit")] double? CreditLimit,
    [property: JsonPropertyName("availableCreditLimit")] double? AvailableCreditLimit,
    [property: JsonPropertyName("validatedMail")] bool? ValidatedMail,
    [property: JsonPropertyName("newCreditButtonEnabled")] bool? NewCreditButtonEnabled,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("mobile")] string? Mobile,
    [property: JsonPropertyName("fullName")] string? FullName,
    [property: JsonPropertyName("defaulter")] bool? Defaulter,
    [property: JsonPropertyName("creditLimitIncrease")] bool? CreditLimitIncrease,
    [property: JsonPropertyName("isAvailableCreditLimit")] bool? IsAvailableCreditLimit,
    [property: JsonPropertyName("isActive")] bool? IsActive,
    [property: JsonPropertyName("status")] int? Status,
    [property: JsonPropertyName("statusName")] string? StatusName,
    [property: JsonPropertyName("firstName")] string? FirstName,
    [property: JsonPropertyName("secondName")] string? SecondName);
