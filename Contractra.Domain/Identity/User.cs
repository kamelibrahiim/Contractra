namespace Contractra.Domain.Identity;

public sealed class User
{
    private readonly List<UserRole> _roles = new();

    public Guid Id { get; private set; }

    public LoginIdentifier LoginIdentifier { get; private set; }

    public UserStatus Status { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    private User()
    {
    }

    public User(LoginIdentifier loginIdentifier, UserStatus status)
        : this(Guid.CreateVersion7(), loginIdentifier, status)
    {
    }

    public User(Guid id, LoginIdentifier loginIdentifier, UserStatus status)
    {
        Id = id;
        LoginIdentifier = loginIdentifier;
        Status = status;
    }

    public void Disable()
    {
        if (Status != UserStatus.Active)
        {
            throw new InvalidOperationException($"Only an Active user can be disabled; current status is {Status}.");
        }

        Status = UserStatus.Disabled;
    }

    public void Reactivate()
    {
        if (Status != UserStatus.Disabled)
        {
            throw new InvalidOperationException($"Only a Disabled user can be reactivated; current status is {Status}.");
        }

        Status = UserStatus.Active;
    }

    /// <summary>
    /// Assigns a role from assignedAt onwards. The caller supplies "now" as asOf;
    /// assignedAt is a historical fact, not a scheduled future start.
    /// A role may be reassigned after an earlier assignment ended, but never so
    /// that two assignments of the same role overlap. Only the assignments loaded
    /// on this instance are checked; the database is the final guard.
    /// </summary>
    public UserRole AssignRole(Role role, DateTimeOffset assignedAt, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (assignedAt > asOf)
        {
            throw new ArgumentException("assignedAt must not be after asOf.", nameof(assignedAt));
        }

        // The new assignment is open-ended, so it overlaps any earlier one of the
        // same role that is still open or ends after the new start.
        if (_roles.Any(r => r.RoleId == role.Id && (r.EndedAt is null || r.EndedAt > assignedAt)))
        {
            throw new InvalidOperationException(
                $"Role '{role.Code}' is already assigned to this user for a period that overlaps {assignedAt:O}.");
        }

        var userRole = new UserRole(Id, role.Id, assignedAt);
        _roles.Add(userRole);
        return userRole;
    }
}
