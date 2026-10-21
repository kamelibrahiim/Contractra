namespace Contractra.Domain.Audit;

/// <summary>
/// The closed catalog of audit event names (auth contract v1.5 §14; repository
/// ADR 0006 §5.3). The enum names are the stored codes and are permanent once
/// written to the append-only table: a later rename can only add a value and
/// keep the old one valid. <c>PasswordReset</c> (replaced in v1.5) and
/// <c>LoginFailed</c> (Serilog only) are deliberately not audit codes.
/// </summary>
public enum AuditActionType
{
    UserCreated,

    /// <summary>Owner decision R46 (not "UserDeactivated").</summary>
    UserDisabled,

    UserReactivated,

    RoleAssigned,

    /// <summary>Owner decision R46 (not "RoleChanged").</summary>
    RoleEnded,

    PermissionGranted,

    PermissionRevoked,

    PayrollViewGranted,

    PayrollViewRevoked,

    PasswordChanged,

    PasswordResetIssued,

    PasswordResetCompleted,

    InitialPasswordReissued,

    BootstrapPasswordReset,

    AccessDenied,

    /// <summary>Operator command <c>clear-throttle</c> (contract §22 M5): manual relief of one identifier's throttle state.</summary>
    ThrottleCleared
}
