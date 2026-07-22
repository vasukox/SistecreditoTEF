using System.Text.Json.Serialization;

namespace SistecreditoTEF.Maui.Dtos;

/// <summary>
/// Body de POST /pos/create.
///
/// Campos obligatorios del manual CREDINET:
///   typeDocument, idDocument, creditValue, frequency, fees, Token (OTP)
///   source (siempre "2"), authMethod (siempre 1)  -> con defaults
/// Opcionales: Seller, products, invoice, storeId.
///
/// C# exige parametros con default DESPUES de los requeridos, por eso
/// Token va antes de Source/AuthMethod.
/// </summary>
public record CreateCreditRequest(
    [property: JsonPropertyName("typeDocument")] string TypeDocument,
    [property: JsonPropertyName("idDocument")]   string IdDocument,
    [property: JsonPropertyName("creditValue")]  double CreditValue,
    [property: JsonPropertyName("frequency")]    int Frequency,
    [property: JsonPropertyName("fees")]         int Fees,
    [property: JsonPropertyName("Token")]        string Token,
    [property: JsonPropertyName("source")]       string Source     = "2",
    [property: JsonPropertyName("authMethod")]   int    AuthMethod = 1,
    [property: JsonPropertyName("Seller")]       string? Seller     = null,
    [property: JsonPropertyName("products")]     string? Products   = null,
    [property: JsonPropertyName("invoice")]      string? Invoice    = null,
    [property: JsonPropertyName("storeId")]      string? StoreId    = null);
