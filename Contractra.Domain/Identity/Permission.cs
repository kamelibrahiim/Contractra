namespace Contractra.Domain.Identity;

public sealed class Permission
{
    public Guid Id { get; private set; }

    public PermissionCode Code { get; private set; }

    public PermissionStatus Status { get; private set; }

    private Permission()
    {
    }

    public Permission(PermissionCode code, PermissionStatus status)
        : this(Guid.CreateVersion7(), code, status)
    {
    }

    public Permission(Guid id, PermissionCode code, PermissionStatus status)
    {
        Id = id;
        Code = code;
        Status = status;
    }

    /// <summary>
    /// Records the documented Active -> Deprecated transition only. What a
    /// deprecated permission means for authorization is not decided yet.
    /// </summary>
    public void Deprecate()
    {
        if (Status != PermissionStatus.Active)
        {
            throw new InvalidOperationException($"Only an Active permission can be deprecated; current status is {Status}.");
        }

        Status = PermissionStatus.Deprecated;
    }
}
