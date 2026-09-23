using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SistecreditoTEF.Maui.Services.Credinet;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services;

/// <summary>
/// Tests del AuthInterceptor: verifica que agrega el header de auth
/// (Ocp-Apim-Subscription-Key) y Accept en cada request.
///
/// HU8-973: el manual NO exige SCLocation (opcional) ni un header "country"
/// (country es un campo de la RESPUESTA, no de la request), y la API funciona
/// sin ellos. Por eso el interceptor no los agrega y no se testean.
/// </summary>
public class AuthInterceptorTests
{
    private sealed class FinalCaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private static ApiConfig Cfg(string key = "my-key") => new()
    {
        SubscriptionKey = key,
        StoreId = null,
        BaseUrl = "https://x"
    };

    [Fact]
    public async Task Adds_Auth_Header()
    {
        var final = new FinalCaptureHandler();
        var auth = new AuthInterceptor(new StaticApiConfigSource(Cfg("k123"))) { InnerHandler = final };
        var http = new HttpClient(auth) { BaseAddress = new System.Uri("https://x/") };

        await http.GetAsync("/dummy");

        Assert.NotNull(final.Captured);
        Assert.Contains("k123", final.Captured!.Headers.GetValues("Ocp-Apim-Subscription-Key"));
    }

    [Fact]
    public async Task Adds_Accept_json_header()
    {
        var final = new FinalCaptureHandler();
        var auth = new AuthInterceptor(new StaticApiConfigSource(Cfg())) { InnerHandler = final };
        var http = new HttpClient(auth) { BaseAddress = new System.Uri("https://x/") };

        await http.GetAsync("/dummy");

        Assert.NotNull(final.Captured);
        var values = final.Captured!.Headers.GetValues("Accept");
        Assert.Contains("application/json", values);
    }

    [Fact]
    public async Task Does_not_send_SCLocation_or_country_headers()
    {
        var final = new FinalCaptureHandler();
        var auth = new AuthInterceptor(new StaticApiConfigSource(Cfg())) { InnerHandler = final };
        var http = new HttpClient(auth) { BaseAddress = new System.Uri("https://x/") };

        await http.GetAsync("/dummy");

        Assert.NotNull(final.Captured);
        Assert.False(final.Captured!.Headers.Contains("SCLocation"));
        Assert.False(final.Captured!.Headers.Contains("country"));
    }
}
