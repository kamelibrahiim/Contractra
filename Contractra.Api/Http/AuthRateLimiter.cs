using System.Threading.RateLimiting;
using Contractra.Api.Auth;
using Contractra.Application.Identity.Auth;
using Contractra.Infrastructure.Persistence.Throttling;

namespace Contractra.Api.Http;

/// <summary>
/// Contract v1.5 §17 layers 1 and 5 - the only layers the contract allows to be the
/// in-memory, per-instance ASP.NET Core rate limiter (System.Threading.RateLimiting
/// fixed windows, partitioned by client IP): layer 1 counts every request of one IP to
/// any public auth endpoint (§4.1), layer 5 every refresh of one IP. Both apply to
/// refresh, because refresh is public. A rejection is 429 auth.too_many_attempts with
/// Retry-After, logged as Auth.Throttled with the layer and the IP.
/// </summary>
public sealed class AuthRateLimiter : IAsyncDisposable
{
    private readonly PartitionedRateLimiter<string> _public;
    private readonly PartitionedRateLimiter<string> _refresh;

    public AuthRateLimiter(AuthThrottleOptions options)
    {
        _public = Create(options.PublicPermitLimit, options.PublicWindow);
        _refresh = Create(options.RefreshPermitLimit, options.RefreshWindow);
    }

    public async ValueTask DisposeAsync()
    {
        await _public.DisposeAsync();
        await _refresh.DisposeAsync();
    }

    /// <returns>The retry delay and layer when rejected, or null when admitted.</returns>
    public (string Layer, TimeSpan RetryAfter)? TryAcquire(string clientIp, bool publicLayer, bool refreshLayer)
    {
        if (publicLayer && Rejected(_public, clientIp) is { } publicRetry)
        {
            return ("1", publicRetry);
        }

        if (refreshLayer && Rejected(_refresh, clientIp) is { } refreshRetry)
        {
            return ("5", refreshRetry);
        }

        return null;
    }

    private static TimeSpan? Rejected(PartitionedRateLimiter<string> limiter, string key)
    {
        using var lease = limiter.AttemptAcquire(key);
        if (lease.IsAcquired)
        {
            return null;
        }

        return lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.FromSeconds(1);
    }

    private static PartitionedRateLimiter<string> Create(int permitLimit, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
}

public sealed class AuthRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthRateLimiter _limiter;
    private readonly ILogger<AuthRateLimitMiddleware> _logger;

    public AuthRateLimitMiddleware(RequestDelegate next, AuthRateLimiter limiter, ILogger<AuthRateLimitMiddleware> logger)
    {
        _next = next;
        _limiter = limiter;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var layers = context.GetEndpoint()?.Metadata.GetMetadata<RateLimitLayersMetadata>();
        if (layers is not null)
        {
            var ip = ClientSource.Of(context);
            if (_limiter.TryAcquire(ip, layers.PublicLayer, layers.RefreshLayer) is { } rejected)
            {
                _logger.LogInformation(
                    "Auth.Throttled Layer={Layer} ClientIp={ClientIp} RetryAfterSeconds={RetryAfter} Action={Action}",
                    rejected.Layer, ip, Math.Ceiling(rejected.RetryAfter.TotalSeconds), $"{context.Request.Method} {context.Request.Path}");
                await Problems.WriteCodeAsync(context, AuthErrorCodes.TooManyAttempts, retryAfter: rejected.RetryAfter);
                return;
            }
        }

        await _next(context);
    }
}
