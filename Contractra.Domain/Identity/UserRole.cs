namespace Contractra.Domain.Identity;

/// <summary>
/// Dated assignment of a Role to a User. Owned by User; created only through
/// User.AssignRole. Ending an assignment records EndedAt; the record is never
/// deleted, and the same role may be assigned again later as a new record.
/// Timestamps are UTC instants, so an ended assignment stops at that moment.
/// </summary>
public sealed class UserRole
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    private UserRole()
    {
    }

    internal UserRole(Guid userId, Guid roleId, DateTimeOffset assignedAt)
    {
        EnsureUtc(assignedAt, nameof(assignedAt));

        Id = Guid.CreateVersion7();
        UserId = userId;
        RoleId = roleId;
        AssignedAt = assignedAt;
    }

    /// <summary>
    /// Active from AssignedAt (inclusive) until EndedAt (exclusive).
    /// </summary>
    public bool IsActiveAt(DateTimeOffset asOf) =>
        asOf >= AssignedAt && (EndedAt is null || asOf < EndedAt);

    /// <summary>
    /// Records the documented Active -> Ended transition. The caller supplies
    /// "now" as asOf; this entity never reads the system clock, and endedAt is
    /// a historical fact, not a scheduled future end.
    /// </summary>
    public void End(DateTimeOffset endedAt, DateTimeOffset asOf)
    {
        EnsureUtc(endedAt, nameof(endedAt));

        if (EndedAt is not null)
        {
            throw new InvalidOperationException($"This role assignment already ended at {EndedAt:O}.");
        }

        if (endedAt > asOf)
        {
            throw new ArgumentException("endedAt must not be after asOf.", nameof(endedAt));
        }

        if (endedAt < AssignedAt)
        {
            throw new ArgumentException("endedAt must not be before AssignedAt.", nameof(endedAt));
        }

        EndedAt = endedAt;
    }

    // Rejected rather than converted: the documented columns are TIMESTAMP_UTC,
    // and silently shifting a caller's value would hide what was supplied.
    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC (offset 00:00).", parameterName);
        }
    }
}
