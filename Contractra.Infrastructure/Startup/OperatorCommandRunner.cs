using Contractra.Application.Identity.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Infrastructure.Startup;

/// <summary>
/// The operator-only command line of contract v1.5 §10A, §11, §18: not an HTTP
/// endpoint, run on the server, one correlation id per run (§13), always with a
/// <c>--reason</c>.
///
/// <code>
/// issue-password-reset --login &lt;identifier&gt; --reason &lt;text&gt;
/// reset-bootstrap-password --reason &lt;text&gt;
/// </code>
///
/// Exit codes: 0 done; 1 refused or failed (nothing changed); 2 bad usage. The
/// secret (a reset code or a new password) goes to <c>output</c> once and is never
/// logged or audited. The audit privilege self-check runs first, like at startup.
/// </summary>
public sealed class OperatorCommandRunner
{
    public const string IssuePasswordReset = "issue-password-reset";
    public const string ResetBootstrapPassword = "reset-bootstrap-password";

    private readonly IServiceScopeFactory _scopes;
    private readonly StartupInitializer _startup;
    private readonly ILogger<OperatorCommandRunner> _logger;

    public OperatorCommandRunner(
        IServiceScopeFactory scopes, StartupInitializer startup, ILogger<OperatorCommandRunner>? logger = null)
    {
        _scopes = scopes;
        _startup = startup;
        _logger = logger ?? NullLogger<OperatorCommandRunner>.Instance;
    }

    public static bool IsOperatorCommand(IReadOnlyList<string> args) =>
        args.Count > 0 && args[0] is IssuePasswordReset or ResetBootstrapPassword;

    public static string Usage =>
        $"""
        Usage:
          {IssuePasswordReset} --login <identifier> --reason <text>
          {ResetBootstrapPassword} --reason <text>

        --reason is required (1-1000 characters, no leading or trailing spaces) and is written to the audit trail: do not put secrets in it.
        The code or password is printed once on this terminal. Do not redirect the output to a file, a ticket or a chat.
        """;

    public async Task<int> RunAsync(
        IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        if (!TryParse(args, out var options, out var problem))
        {
            await error.WriteLineAsync(problem);
            await error.WriteLineAsync(Usage);
            return 2;
        }

        var correlationId = Guid.NewGuid().ToString("D");
        await output.WriteLineAsync($"Correlation id: {correlationId}");

        try
        {
            await _startup.RunPrerequisitesForCommandsAsync(cancellationToken);

            await using var scope = _scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;

            if (options.Command == IssuePasswordReset)
            {
                var result = await services.GetRequiredService<OperatorIssuePasswordResetUseCase>().ExecuteAsync(
                    new OperatorIssuePasswordResetRequest(options.Login!, options.Reason, correlationId), cancellationToken);

                if (!result.IsSuccess)
                {
                    await error.WriteLineAsync($"Refused: {result.Error}. Nothing was changed.");
                    return 1;
                }

                await output.WriteLineAsync($"Reset code (shown once): {result.Value!.Code}");
                await output.WriteLineAsync($"Valid until: {result.Value.ExpiresAt:O}");
                return 0;
            }

            var reset = await services.GetRequiredService<ResetBootstrapPasswordUseCase>().ExecuteAsync(
                new ResetBootstrapPasswordRequest(options.Reason, correlationId), cancellationToken);

            if (!reset.IsSuccess)
            {
                await error.WriteLineAsync($"Refused: {reset.Error}. Nothing was changed.");
                return 1;
            }

            await output.WriteLineAsync($"New bootstrap admin password (shown once): {reset.Value!.NewPassword}");
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Operator command {Command} failed. CorrelationId={CorrelationId}", options.Command, correlationId);
            await error.WriteLineAsync(
                $"The command failed and nothing was committed ({exception.GetType().Name}). See the application log for correlation id {correlationId}.");
            return 1;
        }
    }

    private sealed record Options(string Command, string? Login, string Reason);

    private static bool TryParse(IReadOnlyList<string> args, out Options options, out string problem)
    {
        options = new Options(string.Empty, null, string.Empty);
        problem = string.Empty;

        if (!IsOperatorCommand(args))
        {
            problem = "Unknown command.";
            return false;
        }

        var command = args[0];
        string? login = null;
        string? reason = null;

        for (var i = 1; i < args.Count; i += 2)
        {
            var name = args[i];
            if (i + 1 >= args.Count)
            {
                problem = $"{name} needs a value.";
                return false;
            }

            var value = args[i + 1];
            switch (name)
            {
                case "--login" when command == IssuePasswordReset && login is null:
                    login = value;
                    break;
                case "--reason" when reason is null:
                    reason = value;
                    break;
                default:
                    problem = $"Unexpected or repeated argument {name}.";
                    return false;
            }
        }

        if (command == IssuePasswordReset && string.IsNullOrWhiteSpace(login))
        {
            problem = "--login is required.";
            return false;
        }

        if (reason is null)
        {
            problem = "--reason is required.";
            return false;
        }

        options = new Options(command, login, reason);
        return true;
    }
}
