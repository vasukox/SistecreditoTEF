using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SistecreditoTEF.Maui.Services.Credinet;

namespace SistecreditoTEF.Maui.Tests.Services;

/// <summary>
/// HttpMessageHandler mock que captura el request y devuelve una
/// respuesta vacia 200 OK (no nos interesa la respuesta, solo el request).
///
/// Sirve para verificar EXACTAMENTE que la URL, metodo, headers y body
/// que produce [CredinetApiClient] son lo que esperamos. Si pasan, el
/// cable HTTP de la app es correcto y el problema es del servidor
/// (CREDINET 5xx, IP whitelist, etc.).
/// </summary>
public sealed class CapturingHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public Func<HttpRequestMessage, HttpResponseMessage>? RespondWith { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;

        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        if (RespondWith is not null) return RespondWith(request);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"function\":\"/api/credit/getCreditLimitClient\"," +
                "\"errorCode\":0,\"message\":\"\",\"country\":\"co\",\"data\":null}",
                Encoding.UTF8, "application/json")
        };
    }
}
