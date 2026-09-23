using System.Net;
using System.Text;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services;

/// <summary>
/// Redacción de datos sensibles en el registro de peticiones HTTP.
///
/// Es el control que garantiza que la credencial de Azure APIM, la cédula del
/// cliente y el OTP no queden escritos en ningún lado: ni en el capture en
/// memoria que consulta el modo demo, ni en logcat.
///
/// Importa especialmente en terminales POS con adb habilitado para soporte:
/// cualquiera con acceso USB puede leer logcat.
/// </summary>
public class HttpLoggingRedaccionTests
{
    private const string ClaveApim = "88dec4b8617c4644a239a8af283dc742";
    private const string Cedula = "1026260942";

    private sealed class FakeCapture : IRequestLogCapture
    {
        public List<HttpLogEntry> Entries { get; } = new();
        public void Append(HttpLogEntry entry) => Entries.Add(entry);
        public void Clear() => Entries.Clear();
        IReadOnlyList<HttpLogEntry> IRequestLogCapture.Entries => Entries;
    }

    private sealed class StubInner : HttpMessageHandler
    {
        public string ResponseBody { get; set; } = "{}";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            });
    }

    private static async Task<HttpLogEntry> CapturarAsync(
        HttpRequestMessage request, string responseBody = "{}",
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var capture = new FakeCapture();
        var previous = HttpLoggingHandler.Enabled;
        HttpLoggingHandler.Enabled = true;
        try
        {
            var handler = new HttpLoggingHandler(capture)
            {
                InnerHandler = new StubInner { ResponseBody = responseBody, Status = status }
            };
            using var client = new HttpClient(handler);
            using var _ = await client.SendAsync(request);
            return Assert.Single(capture.Entries);
        }
        finally
        {
            HttpLoggingHandler.Enabled = previous;
        }
    }

    // ------------------------------------------------------------------
    // Credencial de APIM
    // ------------------------------------------------------------------

    [Fact]
    public async Task La_SubscriptionKey_NUNCA_queda_registrada()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.credinet.co/pos/getactivecredits");
        request.Headers.Add(ApiConfig.HeaderSubscriptionKey, ClaveApim);

        var entry = await CapturarAsync(request);

        Assert.DoesNotContain(ClaveApim, entry.RequestHeaders.Values);
        Assert.Equal("***REDACTED***", entry.RequestHeaders[ApiConfig.HeaderSubscriptionKey]);
    }

    [Fact]
    public async Task El_header_Authorization_tambien_se_redacta()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.credinet.co/pos/x");
        request.Headers.Add("Authorization", "Bearer token-secreto");

        var entry = await CapturarAsync(request);

        Assert.DoesNotContain("token-secreto", entry.RequestHeaders.Values);
    }

    [Fact]
    public async Task Los_headers_no_sensibles_se_conservan_para_diagnosticar()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.credinet.co/pos/x");
        request.Headers.Add(ApiConfig.HeaderAccept, ApiConfig.MimeJson);

        var entry = await CapturarAsync(request);

        Assert.Equal(ApiConfig.MimeJson, entry.RequestHeaders[ApiConfig.HeaderAccept]);
    }

    // ------------------------------------------------------------------
    // Cédula del cliente
    // ------------------------------------------------------------------

    [Fact]
    public async Task La_cedula_se_enmascara_en_la_URL()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.credinet.co/pos/getCreditLimitClient?typeDocument=CC&idDocument={Cedula}&storeId=7");

        var entry = await CapturarAsync(request);

        Assert.DoesNotContain(Cedula, entry.Url);
        Assert.Contains("idDocument=******0942", entry.Url);
        // El endpoint sigue siendo legible: la redacción no impide diagnosticar.
        Assert.Contains("getCreditLimitClient", entry.Url);
    }

    [Fact]
    public async Task La_cedula_se_enmascara_en_el_cuerpo_del_POST()
    {
        var body = $"{{\"typeDocument\":\"CC\",\"idDocument\":\"{Cedula}\",\"creditValue\":500000}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.credinet.co/pos/create")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var entry = await CapturarAsync(request);

        Assert.NotNull(entry.RequestBody);
        Assert.DoesNotContain(Cedula, entry.RequestBody!);
        Assert.Contains("******0942", entry.RequestBody!);
        // El resto del cuerpo se conserva.
        Assert.Contains("creditValue", entry.RequestBody!);
    }

    [Fact]
    public async Task La_cedula_se_enmascara_tambien_en_la_RESPUESTA()
    {
        var response = $"{{\"errorCode\":0,\"data\":{{\"idDocument\":\"{Cedula}\"}}}}";
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.credinet.co/pos/x");

        var entry = await CapturarAsync(request, responseBody: response);

        Assert.NotNull(entry.ResponseBody);
        Assert.DoesNotContain(Cedula, entry.ResponseBody!);
    }

    // ------------------------------------------------------------------
    // OTP
    // ------------------------------------------------------------------

    [Fact]
    public async Task El_OTP_se_redacta_del_cuerpo()
    {
        // La clave dinámica no debe quedar registrada en ningún lado: con el OTP y
        // la cédula se puede crear un crédito.
        var body = $"{{\"idDocument\":\"{Cedula}\",\"token\":\"483920\"}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.credinet.co/pos/create")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var entry = await CapturarAsync(request);

        Assert.DoesNotContain("483920", entry.RequestBody!);
        Assert.Contains("***REDACTED***", entry.RequestBody!);
    }

    // ------------------------------------------------------------------
    // El registro sigue siendo útil
    // ------------------------------------------------------------------

    [Fact]
    public async Task Se_registran_metodo_estado_y_duracion()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.credinet.co/pos/payCredit")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        var entry = await CapturarAsync(request, status: HttpStatusCode.BadRequest);

        Assert.Equal("POST", entry.Method);
        Assert.Equal(400, entry.StatusCode);
        Assert.True(entry.DurationMs >= 0);
    }

    [Fact]
    public async Task Con_el_registro_desactivado_no_se_captura_nada()
    {
        // El gate [HttpLoggingHandler.Enabled] es lo que evita acumular en memoria
        // cuerpos de peticiones con datos de clientes.
        var capture = new FakeCapture();
        var previous = HttpLoggingHandler.Enabled;
        try
        {
            HttpLoggingHandler.Enabled = false;
            var handler = new HttpLoggingHandler(capture) { InnerHandler = new StubInner() };

            using var client = new HttpClient(handler);
            using var _ = await client.GetAsync("https://api.credinet.co/pos/x");
        }
        finally
        {
            HttpLoggingHandler.Enabled = previous;
        }

        Assert.Empty(capture.Entries);
    }

    [Fact]
    public void Construir_un_handler_NO_enciende_el_registro()
    {
        // El constructor solía encender el gate como efecto secundario: cualquier
        // código que resolviera el handler del contenedor —sin intención de loguear
        // nada— activaba la captura para toda la app, y volvía imposible apagarla de
        // forma estable porque el siguiente handler la reactivaba.
        //
        // Este test lo detectó de la peor forma posible: como una falla
        // intermitente, porque el test del grafo de DI resuelve el handler en
        // paralelo y encendía el gate a mitad de otro test.
        var previous = HttpLoggingHandler.Enabled;
        try
        {
            HttpLoggingHandler.Enabled = false;

            _ = new HttpLoggingHandler(new FakeCapture());

            Assert.False(HttpLoggingHandler.Enabled,
                "Construir el handler no debe alterar el gate global.");
        }
        finally
        {
            HttpLoggingHandler.Enabled = previous;
        }
    }
}
