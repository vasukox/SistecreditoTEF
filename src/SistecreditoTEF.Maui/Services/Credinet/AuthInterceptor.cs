namespace SistecreditoTEF.Maui.Services.Credinet;

public class AuthInterceptor : DelegatingHandler
{
    private readonly ApiConfig _config;

    public AuthInterceptor(ApiConfig config)
    {
        _config = config;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(ApiConfig.HeaderSubscriptionKey))
            request.Headers.Add(ApiConfig.HeaderSubscriptionKey, _config.SubscriptionKey);
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
