using System.Text.Json.Serialization;

namespace SistecreditoTEF.Maui.Dtos;

/// <summary>
/// Respuesta de getCreditToken. Estructura JSON:
/// {
///   "token": { "value": "...", "remainingSeconds": 62 },
///   "formattedToken": "...",
///   "totalTime": 300,
///   "duration": "00:01:02",
///   "expirationDate": "...",
///   "generationDate": "...",
///   "tokenGenerated": true   (campo no documentado)
/// }
/// </summary>
public record CreditTokenDto(
    [property: JsonPropertyName("token")] TokenDataDto? Token,
    [property: JsonPropertyName("formattedToken")] string? FormattedToken,
    [property: JsonPropertyName("totalTime")] int? TotalTime,
    [property: JsonPropertyName("duration")] string? Duration,
    [property: JsonPropertyName("expirationDate")] string? ExpirationDate,
    [property: JsonPropertyName("generationDate")] string? GenerationDate,
    [property: JsonPropertyName("tokenGenerated")] bool? TokenGenerated = null);

public record TokenDataDto(
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("remainingSeconds")] int? RemainingSeconds);
