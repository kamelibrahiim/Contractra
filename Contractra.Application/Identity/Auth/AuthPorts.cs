using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.Auth;

/// <summary>What an access token states about its holder at issue time (contract §3.2).</summary>
public sealed record AccessTokenSubject(
    Guid UserId,
    Guid SessionId,
    ClientApp App,
    int SecurityVersion,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Roles);

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues signed access tokens (contract §3.2, §3.2.1): JWT, HS256, 30 minutes,
/// signed with the active key of the key ring. Implemented at the HTTP boundary.
/// </summary>
public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(AccessTokenSubject subject, DateTimeOffset issuedAt);
}

/// <summary>A new opaque refresh token and the SHA-256 hash that is the only thing stored (contract §3.1).</summary>
public sealed record NewRefreshToken(string Token, byte[] Hash);

public interface IRefreshTokenFactory
{
    /// <summary>32 bytes from a cryptographically secure generator, base64url.</summary>
    NewRefreshToken Create();

    /// <summary>The hash of a presented token, computed the same way as <see cref="Create"/>.</summary>
    byte[] Hash(string presentedToken);
}

/// <summary>
/// A user's authorization state at a point in time (contract §2): the role
/// assignments and direct grants active at that time, and the permissions each
/// active role carries (for the contract §14.1 actor_role_code rule).
/// </summary>
public sealed record UserAccess(
    IReadOnlyList<string> ActiveRoleCodes,
    IReadOnlyList<string> EffectivePermissions,
    IReadOnlyDictionary<string, IReadOnlyList<string>> PermissionsByActiveRole)
{
    public static readonly UserAccess None = new([], [], new Dictionary<string, IReadOnlyList<string>>());

    public bool HasPermission(string code) => EffectivePermissions.Contains(code, StringComparer.Ordinal);
}

public interface IUserAccessReader
{
    /// <summary>Active roles (sorted by ordinal code order) and effective permissions (sorted) at <paramref name="asOf"/>.</summary>
    Task<UserAccess> GetAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

/// <summary>The two values read on every authenticated request (contract §3.5, §7).</summary>
public sealed record SecurityStamp(int SecurityVersion, bool MustChangePassword);

public interface ISecurityStampReader
{
    /// <summary>
    /// One indexed lookup by user id reading security_version and
    /// must_change_password from the users row (contract §3.5); null when no such user.
    /// </summary>
    Task<SecurityStamp?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Session access needed by login and refresh, beyond <see cref="ISessionStore"/>.</summary>
public interface IAuthSessionRepository
{
    /// <summary>Stages a new session (saved by the caller's unit of work).</summary>
    void Add(AuthSession session);

    /// <summary>
    /// The session owning the token with this hash (consumed or not), tracked,
    /// with that token and the session's unconsumed token loaded; null when unknown.
    /// </summary>
    Task<AuthSession?> FindByRefreshTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default);
}

/// <summary>
/// The transparent rehash of a correct password whose stored hash needs an
/// upgrade (contract §5.1, owner decision R33). The Domain has no rehash method
/// (User.ChangePassword would clear the forced change and bump the security
/// version), so this replaces only the hash string, in the caller's transaction,
/// and only while the stored hash is still the one that was verified.
/// </summary>
public interface IPasswordRehashStore
{
    /// <returns>False when the stored hash changed in the meantime (nothing written).</returns>
    Task<bool> ReplaceHashAsync(Guid userId, string verifiedHash, string newHash, CancellationToken cancellationToken = default);
}

/// <summary>Contract §17 admission outcome for a throttled attempt.</summary>
public enum ThrottleStatus
{
    Admitted,
    Throttled,
    Unavailable,
}

/// <summary>What a later success needs to release an admitted attempt (contract §17, §22 D).</summary>
public sealed record ThrottleTicket(IReadOnlyList<byte[]> Keys, DateTimeOffset AdmittedAt, bool EnteredIdentifierSlowMode);

public sealed record ThrottleAdmission(ThrottleStatus Status, TimeSpan? RetryAfter = null, ThrottleTicket? Ticket = null)
{
    public static ThrottleAdmission Unavailable() => new(ThrottleStatus.Unavailable);
}

/// <summary>
/// The persisted throttle layers of contract §17 (layers 2-4) over the shared store.
/// Layers 1 and 5 are the per-instance HTTP rate limiter. Every admitted attempt is
/// counted at admission; a success releases its own count (layers 3, 4) or clears
/// the pair counter (layer 2). Every other outcome stays counted as a failure.
/// </summary>
public interface IAuthThrottle
{
    /// <summary>Layers 2 and 3 for a login or reset-completion attempt.</summary>
    /// <param name="identifierKey">SHA-256 of the lower-cased submitted identifier (<see cref="ThrottleKeys.IdentifierKey"/>).</param>
    /// <param name="clientSource">The client IP (after the trusted-proxy rules), or "unknown".</param>
    Task<ThrottleAdmission> AdmitCredentialAttemptAsync(byte[] identifierKey, string clientSource, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>The credential was correct: clears layer 2 for the pair and releases the layer 3 count.</summary>
    Task RecordCredentialSuccessAsync(ThrottleTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>Layer 4 for a change-password attempt by this user.</summary>
    Task<ThrottleAdmission> AdmitChangePasswordAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>The attempt was not a wrong current password: releases its layer 4 count.</summary>
    Task RecordChangePasswordNotWrongAsync(ThrottleTicket ticket, CancellationToken cancellationToken = default);
}

/// <summary>
/// Operator relief for layer 3 (contract §22 M5), in the caller's own unit of
/// work: clears the identifier-wide slow-mode state for one identifier, across
/// every source. Does not touch layer 2's per-(identifier, source) pairs (not
/// enumerable by identifier alone) or any other user's or source's state.
/// Deliberately not part of <see cref="IAuthThrottle"/>: every
/// <see cref="IAuthThrottle"/> member runs its own short transaction in its own
/// DI scope (ADR 0005 §3.4) so admission checks are never held open behind a
/// password hash; this port instead joins the caller's transaction (ADR 0006
/// §6) so the clear and its audit event commit or roll back together - the
/// same pattern as <see cref="Contractra.Application.Audit.IAuditWriter"/>. It
/// only writes: it never saves and never opens or commits a transaction.
/// </summary>
public interface IThrottleIdentifierEraser
{
    /// <param name="identifierKey">SHA-256 of the lower-cased submitted identifier (<see cref="ThrottleKeys.IdentifierKey"/>).</param>
    /// <returns>True if a row existed and was removed; false if there was nothing to clear (not an error - the operator's intent is already satisfied).</returns>
    Task<bool> ClearIdentifierAsync(byte[] identifierKey, CancellationToken cancellationToken = default);
}


/// <summary>An AccessDenied audit event to write (contract §6, §14, §14.2).</summary>
/// <param name="DeniedCode">The 403 code returned, e.g. auth.app_access_denied.</param>
/// <param name="Action">What was refused, e.g. "POST /api/v1/auth/login". Never contains a secret.</param>
public sealed record AccessDeniedRecord(
    Guid ActorUserId,
    AuditSource Source,
    string? RequiredPermission,
    string DeniedCode,
    string Action,
    string CorrelationId);

/// <summary>
/// Writes AccessDenied in its own short transaction and scope, never the request's
/// (contract §14.2, ADR 0006 §6). A failure never changes the 403: it is logged
/// as Audit.WriteFailed.
/// </summary>
public interface IAccessDeniedAuditor
{
    Task RecordAsync(AccessDeniedRecord record, CancellationToken cancellationToken = default);
}
