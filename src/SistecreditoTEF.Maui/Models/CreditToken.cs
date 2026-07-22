namespace SistecreditoTEF.Maui.Models;

/// <summary>
/// Modelo de negocio: Token OTP para autorizar un credito.
/// </summary>
public record CreditToken(
    string TokenValue,
    int RemainingSeconds,
    string FormattedToken,
    int TotalTime,
    string Duration,
    string ExpirationDate,
    string GenerationDate,
    bool TokenGenerated = false);
