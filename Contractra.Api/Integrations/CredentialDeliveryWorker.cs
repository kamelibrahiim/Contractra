using Contractra.Application.Identity.Credentials;
using Contractra.Infrastructure.Identity;

namespace Contractra.Api.Integrations;

/// <summary>
/// The delivery worker and sweeper of ADR 0007 §6-§7: polls for due issuances, runs one attempt each with a
/// lease, and sweeps expired or already-used ones. It never generates or stores a password.
/// </summary>
public sealed class CredentialDeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly CredentialDeliveryOptions _options;
    private readonly ILogger<CredentialDeliveryWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public CredentialDeliveryWorker(IServiceScopeFactory scopes, CredentialDeliveryOptions options, ILogger<CredentialDeliveryWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Never the exception object: nothing here may carry a secret.
                _logger.LogError("Credential delivery cycle failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(_options.WorkerPollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One cycle: sweep, then attempt every due issuance. Public so tests can drive it without timers.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using (var sweepScope = _scopes.CreateAsyncScope())
        {
            await sweepScope.ServiceProvider.GetRequiredService<SweepCredentialsUseCase>().ExecuteAsync(cancellationToken);
        }

        IReadOnlyList<Guid> due;
        await using (var listScope = _scopes.CreateAsyncScope())
        {
            var clock = listScope.ServiceProvider.GetRequiredService<Contractra.Application.Common.IClock>();
            due = await listScope.ServiceProvider.GetRequiredService<ICredentialIssuanceStore>()
                .ListDueIdsAsync(clock.UtcNow, 50, cancellationToken);
        }

        foreach (var id in due)
        {
            await using var scope = _scopes.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<DeliverCredentialUseCase>().ExecuteAsync(id, _workerId, cancellationToken);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                // A webhook or the sweeper changed the row first; the next cycle re-evaluates it.
            }
        }
    }
}

public static class CredentialDeliveryRegistration
{
    /// <summary>
    /// Registers the configured sender and the worker. <c>WhatsApp:Sender = Fake</c> registers the fake sender and only
    /// in Development or Testing (anywhere else startup fails); any other value leaves the sender unconfigured, so
    /// create and reset answer 503. The live Meta sender is PS-04b.
    /// </summary>
    public static WebApplicationBuilder AddContractraCredentialDelivery(this WebApplicationBuilder builder)
    {
        if (string.Equals(builder.Configuration["WhatsApp:Sender"], "Fake", StringComparison.OrdinalIgnoreCase))
        {
            FakeSenderRegistration.EnsureAllowed(builder.Environment.EnvironmentName);
            builder.Services.AddSingleton<FakeWhatsAppSender>();
            builder.Services.AddSingleton<IWhatsAppSender>(sp => sp.GetRequiredService<FakeWhatsAppSender>());
        }

        builder.Services.AddSingleton<CredentialDeliveryWorker>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<CredentialDeliveryWorker>());
        return builder;
    }
}
