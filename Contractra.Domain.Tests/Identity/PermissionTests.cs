using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

public class PermissionTests
{
    [Theory]
    [InlineData(PermissionStatus.Active)]
    [InlineData(PermissionStatus.Deprecated)]
    public void Constructor_UsesTheExplicitlySuppliedStatus(PermissionStatus status)
    {
        var permission = new Permission(new PermissionCode("Users.Manage"), status);

        Assert.Equal(status, permission.Status);
        Assert.Equal("Users.Manage", permission.Code.Value);
    }

    [Fact]
    public void Constructor_GeneratesAVersion7Guid_WhenNoIdIsProvided()
    {
        var permission = new Permission(new PermissionCode("Users.Manage"), PermissionStatus.Active);

        Assert.Equal('7', permission.Id.ToString()[14]);
    }

    [Fact]
    public void Constructor_KeepsAnExplicitlySuppliedId()
    {
        var id = Guid.CreateVersion7();

        var permission = new Permission(id, new PermissionCode("Users.Manage"), PermissionStatus.Active);

        Assert.Equal(id, permission.Id);
    }

    [Fact]
    public void Deprecate_MovesActiveToDeprecated()
    {
        var permission = new Permission(new PermissionCode("Users.Manage"), PermissionStatus.Active);

        permission.Deprecate();

        Assert.Equal(PermissionStatus.Deprecated, permission.Status);
    }

    [Fact]
    public void Deprecate_Throws_WhenAlreadyDeprecated()
    {
        var permission = new Permission(new PermissionCode("Users.Manage"), PermissionStatus.Deprecated);

        Assert.Throws<InvalidOperationException>(() => permission.Deprecate());
    }
}
