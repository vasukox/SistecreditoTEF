namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Captura de requests/responses HTTP para inspeccionarlos en demo mode.
///
/// Permite validar exactamente que estamos enviando a CREDINET (URL,
/// headers, query string) y que recibimos (status, body).
///
/// Solo se activa cuando [HttpLoggingHandler.Enabled]=true.
/// </summary>
public interface IRequestLogCapture
{
    IReadOnlyList<HttpLogEntry> Entries { get; }
    void Append(HttpLogEntry entry);
    void Clear();
}

public sealed record HttpLogEntry(
    DateTime Timestamp,
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> RequestHeaders,
    string? RequestBody,
    int StatusCode,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string ResponseBody,
    long DurationMs);