namespace Contractra.Domain.Identity;

/// <summary>
/// Why a session was revoked; each value is a contract event (auth contract
/// v1.5 §3.3, §3.6, §4, §5.3; ADR 0005 §3.1). Absolute expiry is not a
/// revocation: it is derived from the session's absolute expiry.
/// </summary>
public enum SessionRevokeReason
{
    /// <summary>The user logged out of this session (§5.3).</summary>
    Logout,

    /// <summary>No refresh within the idle timeout (§3.6).</summary>
    Idle,

    /// <summary>A refresh token was presented again after the reuse grace (§3.3, R35).</summary>
    RefreshReuse,

    /// <summary>The user no longer holds the session's app-access permission (§4).</summary>
    AppAccessLost,

    /// <summary>The user was disabled (§3.3).</summary>
    UserDisabled,

    /// <summary>A password reset was issued for the user (§3.3, R44).</summary>
    PasswordResetIssued,

    /// <summary>The user completed a password reset (§3.3, E1).</summary>
    PasswordResetCompleted,

    /// <summary>An initial temporary password was re-issued (§3.3, E3).</summary>
    InitialPasswordReissued,

    /// <summary>The user changed their password in another session (§3.3).</summary>
    PasswordChanged
}
