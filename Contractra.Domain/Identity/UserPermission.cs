namespace Contractra.Domain.Identity;

/// <summary>
/// Direct grant of a Permission to a User, on top of any role. Owned by User;
/// created only through User.GrantPermission. Revoking records RevokedAt; the
/// record is never deleted, and the same permission may be granted again later
/// as a new record. This is the only sanctioned path for Payroll.View (BR-235),
/// which Role.GrantPermission rejects. Timestamps are UTC instants.
/// </summary>
public sealed class UserPermission
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid PermissionId { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    private UserPermission()
    {
    }

    internal UserPermission(Guid userId, Guid permissionId, DateTimeOffset grantedAt)
    {
        EnsureUtc(grantedAt, nameof(grantedAt));

        Id = Guid.CreateVersion7();
        UserId = userId;
        PermissionId = permissionId;
        GrantedAt = grantedAt;
    }

    /// <summary>
    /// Active from GrantedAt (inclusive) until RevokedAt (exclusive).
    /// </summary>
    public bool IsActiveAt(DateTimeOffset asOf) =>
        asOf >= GrantedAt && (RevokedAt is null || asOf < RevokedAt);

    /// <summary>
    /// Records the documented Granted -> Revoked transition. The caller supplies
    /// "now" as asOf; this entity never reads the system clock, and revokedAt is
    /// a historical fact, not a scheduled future revocation.
    /// </summary>
    public void Revoke(DateTimeOffset revokedAt, DateTimeOffset asOf)
    {
        EnsureUtc(revokedAt, nameof(revokedAt));

        if (RevokedAt is not null)
        {
            throw new InvalidOperationException($"This permission grant was already revoked at {RevokedAt:O}.");
        }

        if (revokedAt > asOf)
        {
            throw new ArgumentException("revokedAt must not be after asOf.", nameof(revokedAt));
        }

        if (revokedAt < GrantedAt)
        {
            throw new ArgumentException("revokedAt must not be before GrantedAt.", nameof(revokedAt));
        }

        RevokedAt = revokedAt;
    }

    // Rejected rather than converted: stored timestamps are UTC, and silently
    // shifting a caller's value would hide what was supplied.
    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC (offset 00:00).", parameterName);
        }
    }
}
