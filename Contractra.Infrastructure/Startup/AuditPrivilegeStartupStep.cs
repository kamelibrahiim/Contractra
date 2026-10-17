using Contractra.Infrastructure.Persistence;
using Contractra.Infrastructure.Persistence.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Infrastructure.Startup;

/// <param name="FailClosedOnUnsafeAuditPrivileges">
/// True in Production: an unsafe database role stops startup. Elsewhere it is a
/// warning, because development and the tests connect as the table owner
/// (repository ADR 0006 §7.3).
/// </param>
public sealed record StartupOptions(bool FailClosedOnUnsafeAuditPrivileges);

/// <summary>What makes a role unsafe for the audit tables, as readable names (never a connection detail).</summary>
public static class AuditPrivilegePolicy
{
    /// <summary>One entry per unsafe audit table, e.g. <c>audit_events: UPDATE, DELETE, owner</c>. Empty when every table is safe.</summary>
    public static IReadOnlyList<string> Violations(IEnumerable<AuditPrivilegeReport> reports)
    {
        var violations = new List<string>();
        foreach (var report in reports.Where(r => r.IsUnsafe))
        {
            var held = new List<string>();
            if (report.CanUpdate) { held.Add("UPDATE"); }
            if (report.CanDelete) { held.Add("DELETE"); }
            if (report.CanTruncate) { held.Add("TRUNCATE"); }
            if (report.CanTrigger) { held.Add("TRIGGER"); }
            if (report.IsOwner) { held.Add("owner"); }
            if (report.HasOwnerRoleMembership) { held.Add("member of the owner role"); }
            if (report.IsSuperuser) { held.Add("superuser"); }
            violations.Add($"{report.Table}: {string.Join(", ", held)}");
        }

        return violations;
    }
}

/// <summary>
/// Repository ADR 0006 E42, §7.3: before anything writes, check what the
/// connected role can do to the audit tables. In Production any of UPDATE,
/// DELETE, TRUNCATE, TRIGGER, ownership or superuser stops startup; elsewhere it
/// logs a warning. Read-only; it never changes a privilege.
/// </summary>
public sealed class AuditPrivilegeStartupStep : IStartupStep
{
    private readonly StartupOptions _options;

    public AuditPrivilegeStartupStep(StartupOptions options)
    {
        _options = options;
    }

    public string Name => "AuditPrivilegeSelfCheck";

    public int Order => 0;

    public bool IsPrerequisiteForCommands => true;

    public async Task RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken)
    {
        var logger = scopedServices.GetService<ILogger<AuditPrivilegeStartupStep>>() ?? NullLogger<AuditPrivilegeStartupStep>.Instance;
        var dbContext = scopedServices.GetRequiredService<ContractraDbContext>();

        var violations = AuditPrivilegePolicy.Violations(await AuditPrivilegeSelfCheck.QueryAsync(dbContext, cancellationToken));
        if (violations.Count == 0)
        {
            logger.LogInformation("Audit privilege self-check passed.");
            return;
        }

        var summary = string.Join("; ", violations);
        if (_options.FailClosedOnUnsafeAuditPrivileges)
        {
            throw new InvalidOperationException(
                $"The database role used by the application can change or remove audit rows ({summary}). " +
                "Connect with the application role (no ownership, SELECT and INSERT only on the audit tables) - repository ADR 0006 section 7.");
        }

        logger.LogWarning(
            "The database role can change or remove audit rows ({Violations}). Tolerated outside Production; Production refuses to start.",
            summary);
    }
}
