using Polly;
using Polly.Extensions.Http;

namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// HU8-973: política de reintentos para el HttpClient de CREDINET.
///
/// DISEÑO CONSERVADOR (no debe frenar ventas):
///   • Solo reintenta fallos TRANSITORIOS de infraestructura: error de red
///     (HttpRequestException), 5xx y 408. Los errores de NEGOCIO llegan como
///     HTTP 200 + errorCode → Polly NO los reintenta (no son "transient").
///   • Solo reintenta peticiones SEGURAS de repetir: GET idempotentes. Excluye
///     getCreditToken (envía OTP → reintentar mandaría SMS de más) y TODO POST
///     (create / payCredit → reintentar podría duplicar crédito/pago).
///   • 2 reintentos con backoff corto (250ms, 500ms). En el peor caso agrega
///     ~0.75s SOLO cuando hay un blip; una petición exitosa NO se ve afectada.
///   • El HttpClient.Timeout (config) es el techo GLOBAL de toda la operación
///     incluidos los reintentos, así que nunca se cuelga indefinidamente.
/// </summary>
public static class CredinetHttpPolicies
{
    private static readonly IAsyncPolicy<HttpResponseMessage> _retry =
        HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx, 408 y HttpRequestException
            .WaitAndRetryAsync(
                retryCount: 2,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(250 * attempt));

    private static readonly IAsyncPolicy<HttpResponseMessage> _noOp =
        Policy.NoOpAsync<HttpResponseMessage>();

    /// <summary>
    /// Reintentable solo si es GET y NO es getCreditToken (que dispara OTP).
    /// Cualquier POST (create/payCredit) queda fuera para no duplicar efectos.
    /// </summary>
    public static bool IsRetryable(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Get)
            return false;
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        return path.IndexOf("getCreditToken", StringComparison.OrdinalIgnoreCase) < 0;
    }

    public static IAsyncPolicy<HttpResponseMessage> Select(HttpRequestMessage request) =>
        IsRetryable(request) ? _retry : _noOp;
}
