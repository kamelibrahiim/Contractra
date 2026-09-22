namespace Contractra.Domain.Identity;

/// <summary>
/// Grant of a Permission to a Role. Owned by Role; created only through
/// Role.GrantPermission. There is no removal operation: the removal lifecycle
/// (hard delete vs. dated end) is not resolved in the documentation.
/// </summary>
public sealed class RolePermission
{
    public Guid Id { get; private set; }

    public Guid RoleId { get; private set; }

    public Guid PermissionId { get; private set; }

    private RolePermission()
    {
    }

    internal RolePermission(Guid roleId, Guid permissionId)
    {
        Id = Guid.CreateVersion7();
        RoleId = roleId;
        PermissionId = permissionId;
    }
}
