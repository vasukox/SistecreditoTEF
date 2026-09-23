namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// Inyecta la credencial de Azure APIM y los headers estándar en cada petición.
///
/// Lee la configuración por [IApiConfigSource] en cada request, no una instancia
/// capturada al construirse: si CloudLicense entrega la credencial de producción
/// después de que el contenedor resolvió este handler, las peticiones salen con
/// la credencial nueva sin reiniciar el proceso.
///
/// UN SOLO CONSTRUCTOR, a propósito: dos sobrecargas de la misma aridad hacen que
/// el contenedor de DI falle con <c>AmbiguousConstructorException</c>. Para un
/// valor fijo (tests) se usa [StaticApiConfigSource].
/// </summary>
public class AuthInterceptor : DelegatingHandler
{
    private readonly IApiConfigSource _configSource;

    public AuthInterceptor(IApiConfigSource configSource)
    {
        ArgumentNullException.ThrowIfNull(configSource);
        _configSource = configSource;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var config = _configSource.Current;

        if (!request.Headers.Contains(ApiConfig.HeaderSubscriptionKey))
            request.Headers.Add(ApiConfig.HeaderSubscriptionKey, config.SubscriptionKey);
        if (!request.Headers.Contains(ApiConfig.HeaderAccept))
            request.Headers.Add(ApiConfig.HeaderAccept, ApiConfig.MimeJson);

        if (request.Content is not null
            && request.Content.Headers.ContentType is null)
        {
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(ApiConfig.MimeJson);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
