namespace Contractra.Api.Http;

/// <summary>
/// Contract v1.5 §1, §18 (NFR-SEC-008, E14): outside local development the API does not
/// serve plain HTTP and does not redirect from it (a redirect would come after the
/// credentials were already sent in clear). A request that did not arrive over HTTPS -
/// directly, or as reported by a trusted proxy's X-Forwarded-Proto - is aborted without
/// a response. TLS 1.2+ itself is the Kestrel/hosting configuration.
/// </summary>
public sealed class HttpsOnlyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HttpsOnlyMiddleware> _logger;

    public HttpsOnlyMiddleware(RequestDelegate next, ILogger<HttpsOnlyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.IsHttps)
        {
            return _next(context);
        }

        _logger.LogWarning("Http.PlainHttpRefused Path={Path}: the API does not serve plain HTTP outside development.", context.Request.Path);
        context.Abort();
        return Task.CompletedTask;
    }
}
