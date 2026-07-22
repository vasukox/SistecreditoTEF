using System.Text.Json.Serialization;

namespace SistecreditoTEF.Maui.Dtos;

/// <summary>
/// Wrapper generico que TODAS las respuestas de CREDINET tienen.
/// Estructura JSON comun:
/// {
///   "function": "/api/credit/getCreditLimitClient",
///   "errorCode": 0,
///   "message": "",
///   "country": "co",
///   "data": { ... }  // aqui varia segun endpoint
/// }
/// </summary>
public record ApiResponse<T>(
    [property: JsonPropertyName("function")] string? Function,
    [property: JsonPropertyName("errorCode")] int ErrorCode,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("data")] T? Data);
