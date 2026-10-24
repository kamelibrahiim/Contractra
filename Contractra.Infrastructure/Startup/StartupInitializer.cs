using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Infrastructure.Startup;

/// <summary>One unit of startup work. Each step runs in its own DI scope, so its DbContext is discarded afterwards.</summary>
public interface IStartupStep
{
    string Name { get; }

    /// <summary>Steps run in ascending order; the audit privilege self-check is 0 and runs before anything that writes.</summary>
    int Order { get; }

    /// <summary>True for steps that must also pass before an operator command writes anything.</summary>
    bool IsPrerequisiteForCommands { get; }

    Task RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the startup steps strictly in order and stops at the first failure
/// (repository ADR 0006 E42, §12): the audit privilege self-check always
/// precedes the bootstrap seeder, and a failed self-check means the seeder
/// never runs. A failing step's exception propagates, so startup fails.
/// </summary>
public sealed class StartupInitializer
{
    private readonly IReadOnlyList<IStartupStep> _steps;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<StartupInitializer> _logger;

    public StartupInitializer(
        IEnumerable<IStartupStep> steps, IServiceScopeFactory scopes, ILogger<StartupInitializer>? logger = null)
    {
        _steps = steps.OrderBy(s => s.Order).ToList();
        _scopes = scopes;
        _logger = logger ?? NullLogger<StartupInitializer>.Instance;
    }

    /// <summary>The step names in the order they run.</summary>
    public IReadOnlyList<string> StepNames => _steps.Select(s => s.Name).ToList();

    public Task RunAsync(CancellationToken cancellationToken = default) => RunAsync(_steps, cancellationToken);

    /// <summary>Only the steps an operator command also needs (the privilege self-check).</summary>
    public Task RunPrerequisitesForCommandsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(_steps.Where(s => s.IsPrerequisiteForCommands).ToList(), cancellationToken);

    private async Task RunAsync(IReadOnlyList<IStartupStep> steps, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            await using var scope = _scopes.CreateAsyncScope();
            try
            {
                await step.RunAsync(scope.ServiceProvider, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogCritical(exception, "Startup step {Step} failed; startup stops.", step.Name);
                throw;
            }
        }
    }
}
