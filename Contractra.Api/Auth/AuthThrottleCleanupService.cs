using Contractra.Application.Common;
using Contractra.Infrastructure.Persistence.Throttling;

namespace Contractra.Api.Auth;

/// <summary>
/// The periodic cleanup of contract §17 (bounded size): deletes expired, unblocked
/// throttle rows in batches through <see cref="AuthThrottleStore.DeleteExpiredAsync"/>.
/// A failure is logged and retried at the next interval; it never affects requests.
/// </summary>
public sealed class AuthThrottleCleanupService : BackgroundService
{
    private const int MaxBatchesPerRun = 100;

    private readonly IServiceScopeFactory _scopes;
    private readonly AuthThrottleOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<AuthThrottleCleanupService> _logger;

    public AuthThrottleCleanupService(IServiceScopeFactory scopes, AuthThrottleOptions options, IClock clock, ILogger<AuthThrottleCleanupService> logger)
    {
        _scopes = scopes;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>One cleanup run; returns the number of rows deleted.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var deleted = 0;
        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<AuthThrottleStore>();
            var count = await store.DeleteExpiredAsync(_clock.UtcNow, _options.WindowByScope, _options.CleanupBatchSize, cancellationToken);
            deleted += count;
            if (count == 0)
            {
                break;
            }
        }

        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CleanupEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(_options.CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var deleted = await RunOnceAsync(stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation("Auth.ThrottleCleanup Deleted={Deleted}", deleted);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(exception, "Auth.ThrottleCleanupFailed: retried at the next interval.");
            }
        }
    }
}
