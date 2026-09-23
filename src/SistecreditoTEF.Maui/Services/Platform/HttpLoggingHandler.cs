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
    }

    /// <summary>
    /// Gate del registro de peticiones. Apagado en RELEASE: en el APK de producción
    /// no se acumulan en memoria cuerpos con datos de clientes salvo que alguien lo
    /// active a propósito. Encendido en DEBUG para diagnosticar.
    ///
    /// El valor inicial se fija en el inicializador estático, NO en el constructor.
    /// Antes construir un handler ENCENDÍA el registro globalmente como efecto
    /// secundario: cualquier código que resolviera el handler del contenedor —sin
    /// intención de loguear nada— activaba la captura para toda la aplicación.
    /// Además volvía imposible apagarlo de forma estable, porque el siguiente
    /// handler que se construyera lo volvía a prender.
    /// </summary>
    public static bool Enabled { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

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
                RequestBody: MaskBody(requestBody),
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

        // QA A-1: los cuerpos se enmascaran ANTES de guardarse o loguearse. El del
        // POST /create lleva la cedula y el OTP; las respuestas traen el nombre
        // completo del cliente.
        // MaskBody puede devolver null y HttpLogEntry.ResponseBody no es nullable:
        // una respuesta sin cuerpo se guarda como cadena vacia, no como null.
        var maskedResponseBody = MaskBody(responseBody) ?? string.Empty;

        _capture.Append(new HttpLogEntry(
            Timestamp: DateTime.Now,
            Method: request.Method.Method,
            Url: maskedUrl,
            RequestHeaders: requestHeaders,
            RequestBody: MaskBody(requestBody),
            StatusCode: (int)response.StatusCode,
            ResponseHeaders: responseHeaders,
            ResponseBody: maskedResponseBody,
            DurationMs: sw.ElapsedMilliseconds));

#if ANDROID
        if ((int)response.StatusCode >= 400)
        {
            // requestHeaders ya viene con la key redactada.
            var hs = string.Join(", ", requestHeaders.Select(kv => $"{kv.Key}={kv.Value}"));
            Log.Error("HttpLog",
                $"{request.Method} {maskedUrl}\n  HDR: {hs}\n  -> {(int)response.StatusCode} {maskedResponseBody}");
        }
#endif

        return response;
    }

    private static bool IsSensitiveHeader(string name) =>
        name.Equals("Ocp-Apim-Subscription-Key", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Enmascara la cédula (idDocument) en la query string. QA A-1: delega en
    /// [PiiMask] para que el enmascarado sea uno solo en todo el proyecto.
    /// </summary>
    private static string MaskUrl(string url) => Common.PiiMask.Url(url);

    /// <summary>
    /// QA A-1: enmascara la cédula y el token OTP en los cuerpos JSON.
    ///
    /// Antes los bodies se guardaban CRUDOS: el del POST /create lleva
    /// <c>idDocument</c> y <c>token</c> (el OTP), y el de las respuestas trae el
    /// nombre completo del cliente. Todo eso quedaba en el capture en memoria que
    /// el modo demo muestra en pantalla, y en logcat para las respuestas 4xx/5xx.
    /// </summary>
    private static string? MaskBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return body;

        var masked = System.Text.RegularExpressions.Regex.Replace(
            body,
            "(\"idDocument\"\\s*:\\s*\")([^\"]+)(\")",
            m => m.Groups[1].Value + Common.PiiMask.Document(m.Groups[2].Value) + m.Groups[3].Value,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // El OTP no debe quedar registrado en ningún lado.
        masked = System.Text.RegularExpressions.Regex.Replace(
            masked,
            "(\"token\"\\s*:\\s*\")([^\"]+)(\")",
            m => m.Groups[1].Value + "***REDACTED***" + m.Groups[3].Value,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return masked;
    }
}