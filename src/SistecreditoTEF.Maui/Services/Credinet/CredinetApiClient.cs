using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Dtos;

namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// Implementacion de [ICredinetApi] usando HttpClient + System.Text.Json.
///
/// El interceptor [AuthInterceptor] ya agrego el header Ocp-Apim-Subscription-Key;
/// esta clase solo construye URIs y deserializa el body.
///
/// Notas:
///  - Timeouts configurados via IHttpClientBuilder.AddHttpMessageHandler.
///  - JsonSerializerOptions reusado (no se crea por request).
///  - BuildQueryString: helper que escapa valores para evitar inyeccion de parametros.
/// </summary>
public class CredinetApiClient : ICredinetApi
{
    private readonly HttpClient _http;
    private readonly ApiConfig _config;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public CredinetApiClient(HttpClient http, ApiConfig config)
    {
        _http = http;
        _config = config;

        if (_http.BaseAddress is null && !string.IsNullOrEmpty(_config.BaseUrl))
        {
            _http.BaseAddress = new Uri(_config.BaseUrl);
        }
    }

    public async Task<ApiResponse<ClientDto>> GetCreditLimitClientAsync(
        string typeDocument, string idDocument, string? storeId)
        => await SendAsync<ClientDto>(
            HttpMethod.Get,
            $"getCreditLimitClient?{BuildQs("typeDocument", typeDocument, "idDocument", idDocument, "storeId", storeId)}");

    public async Task<ApiResponse<CreditDetailsDto>> GetCreditDetailsAsync(
        double creditValue, int frequency, int months,
        string typeDocument, string idDocument, string? storeId)
        => await SendAsync<CreditDetailsDto>(
            HttpMethod.Get,
            $"getCreditDetails?{BuildQs(
                "creditValue",  creditValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "frequency",    frequency.ToString(),
                "months",       months.ToString(),
                "typeDocument", typeDocument,
                "idDocument",   idDocument,
                "storeId",      storeId)}");

    public async Task<ApiResponse<CreditTokenDto>> GetCreditTokenAsync(
        double creditValue, int months, int frequency,
        string typeDocument, string idDocument, int? destination,
        string? storeId)
        => await SendAsync<CreditTokenDto>(
            HttpMethod.Get,
            $"getCreditToken?{BuildQs(
                "creditValue",  creditValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "months",       months.ToString(),
                "frequency",    frequency.ToString(),
                "typeDocument", typeDocument,
                "idDocument",   idDocument,
                "destination",  destination?.ToString(),
                "storeId",      storeId)}");

    public async Task<ApiResponse<CreditDto>> CreateAsync(CreateCreditRequest request)
        => await PostAsync<CreditDto, CreateCreditRequest>("create", request);

    public async Task<ApiResponse<List<ActiveCreditDto>>> GetActiveCreditsAsync(
        string typeDocument, string idDocument, string? storeId)
        => await SendAsync<List<ActiveCreditDto>>(
            HttpMethod.Get,
            $"getactivecredits?{BuildQs(
                "typeDocument", typeDocument,
                "idDocument",   idDocument,
                "storeId",      storeId)}");

    public async Task<ApiResponse<PaymentDto>> PayCreditAsync(PayCreditRequest request)
        => await PostAsync<PaymentDto, PayCreditRequest>("payCredit", request);

    public async Task<ApiResponse<SimulatedMonthLimitDto>> GetSimulatedMonthLimitAsync(
        double creditValue, string? storeId)
        => await SendAsync<SimulatedMonthLimitDto>(
            HttpMethod.Get,
            $"getSimulatedMonthLimit?{BuildQs(
                "creditValue", creditValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "storeId",     storeId)}");

    private async Task<ApiResponse<T>> SendAsync<T>(HttpMethod method, string pathWithQuery)
    {
        return await ExecuteAsync<T>(new HttpRequestMessage(method, pathWithQuery));
    }

    private async Task<ApiResponse<TResp>> PostAsync<TResp, TReq>(string path, TReq body)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, ApiConfig.MimeJson)
        };
        return await ExecuteAsync<TResp>(request);
    }

    private static string BuildQs(params string?[] pairs)
    {
        var sb = new System.Text.StringBuilder();
        // Se recorre hasta pairs.Length - 1 para que un arreglo de longitud IMPAR no
        // salga por IndexOutOfRange al leer pairs[i + 1]: el ultimo elemento sin
        // pareja simplemente no aporta parametro.
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            var key = pairs[i];
            var value = pairs[i + 1];
            // La clave tambien se comprueba: EscapeDataString(null) lanza
            // ArgumentNullException, y eso tumbaria la llamada entera por armar mal
            // un query string.
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }
        return sb.ToString();
    }

    private async Task<ApiResponse<T>> ExecuteAsync<T>(HttpRequestMessage request)
    {
        try
        {
            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();

                // ─────────────────────────────────────────────────────────────
                // CREDINET DEVUELVE ERRORES DE NEGOCIO CON HTTP 4xx
                // ─────────────────────────────────────────────────────────────
                // El modulo asumia que los errores de negocio llegan SIEMPRE como
                // HTTP 200 + errorCode. No es asi. Capturado en el terminal:
                //
                //   HTTP 400 {"function":"/api/credit/create","errorCode":230,
                //             "message":"TokenAlreadyUsed","country":"co"}
                //
                // Clasificarlo como fallo de transporte tenia tres efectos malos:
                // el cajero veia "Error de comunicacion con el servidor" en vez de
                // "ese codigo ya se uso"; no se consumia intento, asi que podia
                // machacar el mismo codigo muerto indefinidamente (se vieron 8
                // intentos seguidos); y la traduccion por errorCode nunca se
                // aplicaba.
                //
                // El sobre de error es IDENTICO al de exito, asi que si el cuerpo
                // trae un errorCode se devuelve como respuesta normal y el
                // repositorio lo convierte en ApiError.Business, que es lo que es.
                var negocio = TryParseCredinetEnvelope<T>(body);
                if (negocio is not null)
                {
                    AppLogger.W("CredinetApiClient",
                        $"Credinet respondio HTTP {(int)response.StatusCode} con un error de " +
                        $"NEGOCIO: errorCode={negocio.ErrorCode} message={negocio.Message} " +
                        $"({Describe(request)}).");
                    return negocio;
                }

                // No es un sobre de Credinet: es un fallo real de transporte
                // (gateway, 401 de APIM, HTML de error, etc.).
                throw new ApiException.HttpException((int)response.StatusCode, body);
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOpts);
            if (apiResponse is null)
            {
                throw new ApiException.HttpException(
                    (int)HttpStatusCode.InternalServerError,
                    "Respuesta vacia de CREDINET.");
            }
            return apiResponse;
        }
        catch (ApiException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            SistecreditoTEF.Maui.Common.AppLogger.E(
                "CredinetApiClient",
                // QA A-1: la URL lleva idDocument=<cedula> en la query. Antes se
                // logueaba cruda en estos cuatro catch, saltandose el enmascarado
                // que si aplicaba HttpLoggingHandler.
                $"Network error en {request.Method} {Describe(request)}: {ex.Message}", ex);
            throw new ApiException.NetworkException(ex);
        }
        catch (TaskCanceledException ex)
        {
            SistecreditoTEF.Maui.Common.AppLogger.E(
                "CredinetApiClient",
                $"Timeout en {request.Method} {Describe(request)}: {ex.Message}", ex);
            throw new ApiException.NetworkException(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            SistecreditoTEF.Maui.Common.AppLogger.E(
                "CredinetApiClient",
                $"JSON invalido en {request.Method} {Describe(request)}: {ex.Message}", ex);
            throw new ApiException.NetworkException(ex);
        }
        catch (Exception ex)
        {
            SistecreditoTEF.Maui.Common.AppLogger.E(
                "CredinetApiClient",
                $"Excepcion inesperada en {request.Method} {Describe(request)}: {ex.Message}", ex);
            throw new ApiException.NetworkException(ex);
        }
    }

    /// <summary>
    /// URL de la peticion con la cedula ENMASCARADA, para poder loguear el
    /// endpoint que fallo sin exponer el dato personal (QA A-1).
    /// </summary>
    private static string Describe(HttpRequestMessage request) =>
        SistecreditoTEF.Maui.Common.PiiMask.Url(request.RequestUri?.ToString());

    /// <summary>
    /// Intenta leer el cuerpo como el sobre estandar de Credinet. Devuelve null si
    /// no lo es (o si no trae errorCode), para que el llamador lo trate como fallo
    /// de transporte.
    ///
    /// Se exige <c>errorCode != 0</c>: un 4xx con errorCode 0 no es un error de
    /// negocio identificable y merece tratarse como problema tecnico.
    /// </summary>
    internal static ApiResponse<T>? TryParseCredinetEnvelope<T>(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            var envelope = JsonSerializer.Deserialize<ApiResponse<T>>(body, JsonOpts);
            return envelope is { ErrorCode: not 0 } ? envelope : null;
        }
        catch (JsonException)
        {
            // Cuerpo que no es JSON (HTML de un gateway, texto plano, vacio).
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Excepciones lanzadas por la capa HTTP. El Repository las traduce
    /// a ApiError en su ProcessResponse. Esto mantiene ICredinetApi
    /// con un contrato limpio (retorna ApiResponse&lt;T&gt; crudo) y
    /// separa las responsabilidades: HTTP vs. negocio.
    /// </summary>
    public abstract class ApiException : Exception
    {
        protected ApiException() { }
        protected ApiException(string message) : base(message) { }
        protected ApiException(string message, Exception inner) : base(message, inner) { }

        public sealed class NetworkException : ApiException
        {
            public Exception Cause { get; }
            public NetworkException(Exception cause) : base(cause.Message, cause)
            {
                Cause = cause;
            }
        }

        public sealed class HttpException : ApiException
        {
            public int Code { get; }
            public HttpException(int code, string technicalMessage) : base(technicalMessage)
            {
                Code = code;
            }
        }
    }
}
