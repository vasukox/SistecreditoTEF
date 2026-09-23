namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Errores categorizados para mostrar al usuario.
/// Equivalente 1:1 al sealed class ApiError de Kotlin.
///
/// Vive en su propio archivo (no dentro de ApiResult) para que pueda
/// existir sin acoplar al resultado. La UI hace `is ApiError.Network -> ...`
/// y muestra userMessage en pantalla.
/// </summary>
public abstract record ApiError(string UserMessage)
{
    /// <summary>
    /// Sin conexion / timeout / DNS fail.
    /// </summary>
    public sealed record Network(Exception Cause)
        : ApiError("Sin conexion. Verifica tu red e intenta de nuevo.");

    /// <summary>
    /// El servidor respondio con codigo HTTP no exitoso (401, 500, etc.).
    /// </summary>
    public sealed record Http(int Code, string TechnicalMessage)
        : ApiError(BuildMessage(Code, TechnicalMessage));

    private static string BuildMessage(int code, string technical) =>
        code switch
        {
            400 => $"Peticion rechazada por el servidor (HTTP 400). {Trim(technical)}",
            401 => $"No autorizado (HTTP 401). Verifica la SubscriptionKey.",
            403 => $"Acceso denegado (HTTP 403). {Trim(technical)}",
            404 => $"Recurso no encontrado (HTTP 404). {Trim(technical)}",
            500 => $"Error interno del servidor (HTTP 500). {Trim(technical)}",
            _   => $"Error del servidor (HTTP {code}). {Trim(technical)}"
        };

    private static string Trim(string s) =>
        string.IsNullOrWhiteSpace(s) ? "Intenta mas tarde." : (s.Length > 200 ? s[..200] + "..." : s);

    /// <summary>
    /// CREDINET devolvio 200 OK pero con errorCode != 0 (cliente no existe,
    /// monto fuera de rango, etc.). Es un fallo logico, no tecnico.
    /// </summary>
    public sealed record Business(int Code, string Message, string? Function = null)
        : ApiError($"CREDINET: {Message}");

    /// <summary>
    /// QA: fallo generado por la propia app, sin haber consultado a CREDINET
    /// (p. ej. un cache de idempotencia incompleto, o una configuracion
    /// invalida). Antes estos casos se disfrazaban de [Business], y el mensaje
    /// que veia el cajero empezaba con "CREDINET:", culpando al proveedor de un
    /// problema local.
    /// </summary>
    public sealed record Local(string Message) : ApiError(Message);
}
