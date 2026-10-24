using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Application.Identity.Operations;

public enum BootstrapSeedOutcome
{
    /// <summary>The bootstrap admin was created and its three audit events committed.</summary>
    Created,

    /// <summary>An Active user already holds the Admin role: nothing was read from configuration or written.</summary>
    AdminAlreadyExists,

    /// <summary>The login identifier or password is not configured: warning logged, nothing written.</summary>
    NotConfigured,

    /// <summary>The configured values break a contract rule: error logged naming the rule (never the value), nothing written.</summary>
    InvalidConfiguration,
}

/// <param name="LoginIdentifier">Configuration <c>Bootstrap:AdminLoginIdentifier</c>, or null.</param>
/// <param name="Password">Configuration <c>Bootstrap:AdminPassword</c>, or null. Never logged or audited.</param>
/// <param name="CorrelationId">One per seeder run (contract §13).</param>
public sealed record SeedBootstrapAdminRequest(string? LoginIdentifier, string? Password, string CorrelationId);

/// <summary>
/// Auth contract v1.5 §11 "Creation", owner decisions D2, C3, R19, R26, R41;
/// repository ADR 0006 §5.3, §12 (proposed). Created once, only when no
/// Active user holds the Admin role. Never overwrites an existing account.
///
/// Atomicity: the user, its Admin role, its direct Payroll.View grant and the
/// three audit events (<c>UserCreated</c>, <c>RoleAssigned</c>,
/// <c>PayrollViewGranted</c>; System actor, source SystemJob, fixed reason,
/// idempotency key <c>&lt;action&gt;:bootstrap:&lt;user id&gt;</c>) commit in one
/// transaction, or none does and the exception propagates so startup fails
/// (§14.2). The "no Active Admin" check is the business-operation
/// idempotency; the audit key only guards against a duplicated audit row.
/// </summary>
public sealed class SeedBootstrapAdminUseCase
{
    public const string AdminRoleCode = "Admin";
    public const string SeedReason = "Initial bootstrap administrator created at application startup.";

    private const int MaxLoginIdentifierLength = 256;
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 128;

    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IPermissionRepository _permissions;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SeedBootstrapAdminUseCase> _logger;

    public SeedBootstrapAdminUseCase(
        IUserRepository users,
        IRoleRepository roles,
        IPermissionRepository permissions,
        IPasswordHasher hasher,
        IAuditWriter auditWriter,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<SeedBootstrapAdminUseCase>? logger = null)
    {
        _users = users;
        _roles = roles;
        _permissions = permissions;
        _hasher = hasher;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<SeedBootstrapAdminUseCase>.Instance;
    }

    public async Task<BootstrapSeedOutcome> ExecuteAsync(
        SeedBootstrapAdminRequest request, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        // The configuration is only needed until the first admin exists (§11),
        // so this check comes first and a missing configuration is then fine.
        if (await _users.AnyActiveUserWithRoleAsync(AdminRoleCode, now, cancellationToken))
        {
            _logger.LogInformation("Bootstrap seeding skipped: an active Admin already exists. CorrelationId={CorrelationId}", request.CorrelationId);
            return BootstrapSeedOutcome.AdminAlreadyExists;
        }

        if (string.IsNullOrWhiteSpace(request.LoginIdentifier) || string.IsNullOrEmpty(request.Password))
        {
            _logger.LogWarning(
                "Bootstrap seeding skipped: Bootstrap:AdminLoginIdentifier and Bootstrap:AdminPassword must both be configured. CorrelationId={CorrelationId}",
                request.CorrelationId);
            return BootstrapSeedOutcome.NotConfigured;
        }

        if (request.LoginIdentifier != request.LoginIdentifier.Trim() || request.LoginIdentifier.Length > MaxLoginIdentifierLength)
        {
            _logger.LogError(
                "Bootstrap seeding refused: Bootstrap:AdminLoginIdentifier must have no leading or trailing whitespace and at most {Max} characters. CorrelationId={CorrelationId}",
                MaxLoginIdentifierLength, request.CorrelationId);
            return BootstrapSeedOutcome.InvalidConfiguration;
        }

        if (request.Password.Length < MinPasswordLength || request.Password.Length > MaxPasswordLength)
        {
            _logger.LogError(
                "Bootstrap seeding refused: Bootstrap:AdminPassword must be {Min} to {Max} characters. CorrelationId={CorrelationId}",
                MinPasswordLength, MaxPasswordLength, request.CorrelationId);
            return BootstrapSeedOutcome.InvalidConfiguration;
        }

        if (await _users.ExistsWithLoginIdentifierAsync(request.LoginIdentifier, cancellationToken))
        {
            _logger.LogError(
                "Bootstrap seeding refused: Bootstrap:AdminLoginIdentifier is already used by an account that is not an active Admin. CorrelationId={CorrelationId}",
                request.CorrelationId);
            return BootstrapSeedOutcome.InvalidConfiguration;
        }

        var adminRole = await _roles.FindByCodeAsync(AdminRoleCode, cancellationToken)
            ?? throw new InvalidOperationException("The Admin role is not in the catalog; apply the migrations before starting.");
        var payrollView = await _permissions.FindByCodeAsync(PermissionCode.PayrollView, cancellationToken)
            ?? throw new InvalidOperationException("The Payroll.View permission is not in the catalog; apply the migrations before starting.");

        var user = User.CreateBootstrapAdmin(
            new LoginIdentifier(request.LoginIdentifier), _hasher.Hash(request.Password), adminRole, payrollView, now, now);
        var assignment = user.Roles.Single();
        var grant = user.Permissions.Single();

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        _users.Add(user);

        var operationKey = $"bootstrap:{user.Id}";

        _auditWriter.Stage(AuditEvent.ForSystem(
            AuditActionType.UserCreated, "User", user.Id, now, request.CorrelationId, operationKey, SeedReason));

        _auditWriter.Stage(AuditEvent.ForSystem(
            AuditActionType.RoleAssigned, "User", user.Id, now, request.CorrelationId, operationKey, SeedReason,
            snapshot: new Dictionary<string, string?>
            {
                ["user_role_id"] = assignment.Id.ToString(),
                ["role_id"] = adminRole.Id.ToString(),
                ["role_code"] = adminRole.Code.Value,
                ["assigned_at"] = assignment.AssignedAt.ToString("O"),
                ["ended_at"] = null,
            }));

        // Payroll.View is audited as PayrollViewGranted, never PermissionGranted (§14, R41).
        _auditWriter.Stage(AuditEvent.ForSystem(
            AuditActionType.PayrollViewGranted, "User", user.Id, now, request.CorrelationId, operationKey, SeedReason,
            snapshot: new Dictionary<string, string?>
            {
                ["user_permission_id"] = grant.Id.ToString(),
                ["permission_id"] = payrollView.Id.ToString(),
                ["permission_code"] = payrollView.Code.Value,
                ["granted_at"] = grant.GrantedAt.ToString("O"),
                ["revoked_at"] = null,
            }));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);

        _logger.LogInformation(
            "Bootstrap admin created. CorrelationId={CorrelationId} UserId={UserId}", request.CorrelationId, user.Id);
        return BootstrapSeedOutcome.Created;
    }
}
