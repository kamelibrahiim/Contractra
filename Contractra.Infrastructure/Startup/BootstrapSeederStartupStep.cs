using Contractra.Application.Identity;
using Contractra.Application.Identity.Operations;
using Contractra.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Infrastructure.Startup;

/// <summary>
/// Contract v1.5 §11: runs the bootstrap seeder at startup with a new
/// correlation id for the run, reading <c>Bootstrap:AdminLoginIdentifier</c> and
/// <c>Bootstrap:AdminPassword</c> from configuration (user-secrets or
/// environment variables; never from a file in the repository).
///
/// A failure (including a failed audit write) propagates and stops startup.
/// One exception is handled: when several instances start together, the loser's
/// insert is refused by the database (unique login identifier or the single
/// bootstrap-admin index) - if an Active Admin exists afterwards, the other
/// instance did the work and this is a no-op; otherwise the failure stands.
/// </summary>
public sealed class BootstrapSeederStartupStep : IStartupStep
{
    private readonly IConfiguration _configuration;
    private readonly IClock _clock;

    public BootstrapSeederStartupStep(IConfiguration configuration, IClock clock)
    {
        _configuration = configuration;
        _clock = clock;
    }

    public string Name => "BootstrapAdminSeeder";

    public int Order => 100;

    public bool IsPrerequisiteForCommands => false;

    public async Task RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken)
    {
        var logger = scopedServices.GetService<ILogger<BootstrapSeederStartupStep>>() ?? NullLogger<BootstrapSeederStartupStep>.Instance;
        var request = new SeedBootstrapAdminRequest(
            _configuration["Bootstrap:AdminLoginIdentifier"],
            _configuration["Bootstrap:AdminPassword"],
            Guid.NewGuid().ToString("D"));

        try
        {
            await scopedServices.GetRequiredService<SeedBootstrapAdminUseCase>().ExecuteAsync(request, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Fresh scope: the failed one still tracks the rejected rows.
            await using var scope = scopedServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            if (!await users.AnyActiveUserWithRoleAsync(SeedBootstrapAdminUseCase.AdminRoleCode, _clock.UtcNow, cancellationToken))
            {
                throw;
            }

            logger.LogInformation(
                "Bootstrap seeding lost a race with another instance; an active Admin exists. CorrelationId={CorrelationId}",
                request.CorrelationId);
        }
    }
}
