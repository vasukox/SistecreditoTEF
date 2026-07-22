namespace SistecreditoTEF.Maui.Services.Hiopos.Models;

/// <summary>
/// Constantes de TransactionResult que HioPosCloud espera recibir.
/// Spec ICG doc §4: ACCEPTED, FAILED, UNKNOWN_RESULT.
/// </summary>
public enum TransactionResult
{
    Accepted,
    Failed,
    UnknownResult
}

public static class TransactionResultExtensions
{
    /// <summary>
    /// Serializa el enum al wire format exacto que HioPosCloud espera.
    /// LSP: si se agrega un valor nuevo al enum, esto lanza en lugar de
    /// retornar silenciosamente "UNKNOWN_RESULT" (mantiene consistencia
    /// con el resto de extensiones del proyecto).
    /// </summary>
    public static string ToWire(this TransactionResult r) => r switch
    {
        TransactionResult.Accepted      => "ACCEPTED",
        TransactionResult.Failed        => "FAILED",
        TransactionResult.UnknownResult => "UNKNOWN_RESULT",
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, "TransactionResult no soportado")
    };
}
