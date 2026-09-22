namespace Contractra.Domain.Identity;

public sealed class Role
{
    private readonly List<RolePermission> _permissions = new();

    public Guid Id { get; private set; }

    public RoleCode Code { get; private set; }

    public RoleStatus Status { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    private Role()
    {
    }

    public Role(RoleCode code, RoleStatus status)
        : this(Guid.CreateVersion7(), code, status)
    {
    }

    public Role(Guid id, RoleCode code, RoleStatus status)
    {
        Id = id;
        Code = code;
        Status = status;
    }

    /// <summary>
    /// Grants a permission to this role.
    /// Payroll.View can never be granted through a role (BR-235 / ح-235,
    /// FR-AUTH-006) - the database enforces the same rule independently.
    /// Duplicate detection covers the grants loaded on this instance; the
    /// database unique constraint on (role_id, permission_id) is the final guard.
    /// </summary>
    public RolePermission GrantPermission(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        if (permission.Code.IsPayrollView)
        {
            throw new InvalidOperationException(
                $"'{PermissionCode.PayrollView}' cannot be granted through a role; it is granted explicitly per user.");
        }

        if (_permissions.Any(p => p.PermissionId == permission.Id))
        {
            throw new InvalidOperationException(
                $"Permission '{permission.Code}' is already granted to role '{Code}'.");
        }

        var rolePermission = new RolePermission(Id, permission.Id);
        _permissions.Add(rolePermission);
        return rolePermission;
    }

    /// <summary>
    /// Records the documented Active -> Deprecated transition only. What a
    /// deprecated role means for authorization is not decided yet.
    /// </summary>
    public void Deprecate()
    {
        if (Status != RoleStatus.Active)
        {
            throw new InvalidOperationException($"Only an Active role can be deprecated; current status is {Status}.");
        }

        Status = RoleStatus.Deprecated;
    }
}
