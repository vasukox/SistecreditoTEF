using System.Diagnostics;
#if ANDROID
using Android.Util;
#endif

namespace SistecreditoTEF.Maui.Services.Platform;

public class HttpLoggingHandler : DelegatingHandler
{
    private readonly IRequestLogCapture _capture;

    public HttpLoggingHandler(IRequestLogCapture capture)
    {
        _capture = capture;
#if DEBUG
        Enabled = true;
#endif
    }

    public static bool Enabled { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!Enabled)
            return await base.SendAsync(request, cancellationToken);

        var sw = Stopwatch.StartNew();

        // HU8-973 (seguridad): NUNCA loguear la Subscription-Key (manual §6.2).
        // Se redacta el header sensible y se enmascara la cédula en la URL.
        var requestHeaders = request.Headers.ToDictionary(
            h => h.Key,
            h => IsSensitiveHeader(h.Key) ? "***REDACTED***" : string.Join(",", h.Value));
        var maskedUrl = MaskUrl(request.RequestUri?.ToString() ?? "?");
        string? requestBody = null;
        if (request.Content is not null)
        {
            requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            request.Content = new StringContent(
                requestBody,
                System.Text.Encoding.UTF8,
                request.Content.Headers.ContentType?.MediaType ?? "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _capture.Append(new HttpLogEntry(
                Timestamp: DateTime.Now,
                Method: request.Method.Method,
                Url: maskedUrl,
                RequestHeaders: requestHeaders,
                RequestBody: requestBody,
                StatusCode: 0,
                ResponseHeaders: new Dictionary<string, string>(),
                ResponseBody: $"EXCEPCION: {ex.Message}",
                DurationMs: sw.ElapsedMilliseconds));
#if ANDROID
            Log.Error("HttpLog",
                $"{request.Method} {maskedUrl} EXCEPCION: {ex.Message}");
#endif
            throw;
        }

        sw.Stop();

        var responseHeaders = response.Headers
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        _capture.Append(new HttpLogEntry(
            Timestamp: DateTime.Now,
            Method: request.Method.Method,
            Url: maskedUrl,
            RequestHeaders: requestHeaders,
            RequestBody: requestBody,
            StatusCode: (int)response.StatusCode,
            ResponseHeaders: responseHeaders,
            ResponseBody: responseBody,
            DurationMs: sw.ElapsedMilliseconds));

#if ANDROID
        if ((int)response.StatusCode >= 400)
        {
            // requestHeaders ya viene con la key redactada.
            var hs = string.Join(", ", requestHeaders.Select(kv => $"{kv.Key}={kv.Value}"));
            Log.Error("HttpLog",
                $"{request.Method} {maskedUrl}\n  HDR: {hs}\n  -> {(int)response.StatusCode} {responseBody}");
        }
#endif

        return response;
    }

    private static bool IsSensitiveHeader(string name) =>
        name.Equals("Ocp-Apim-Subscription-Key", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase);

    /// <summary>Enmascara la cédula (idDocument) en la query string.</summary>
    private static string MaskUrl(string url) =>
        System.Text.RegularExpressions.Regex.Replace(
            url, @"(idDocument=)(\d+)",
            m => m.Groups[1].Value + Mask(m.Groups[2].Value),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string Mask(string value) =>
        value.Length <= 4 ? new string('*', value.Length)
                          : new string('*', value.Length - 4) + value[^4..];
}