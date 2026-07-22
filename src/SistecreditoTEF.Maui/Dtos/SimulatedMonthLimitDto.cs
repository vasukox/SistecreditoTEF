using System.Text.Json.Serialization;

namespace SistecreditoTEF.Maui.Dtos;

/// <summary>
/// Respuesta de GET /pos/getSimulatedMonthLimit.
/// </summary>
public record SimulatedMonthLimitDto(
    [property: JsonPropertyName("months")] int? Months);
