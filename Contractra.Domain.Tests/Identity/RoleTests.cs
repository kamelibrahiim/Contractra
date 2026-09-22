using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

public class RoleTests
{
    private static Role NewRole(RoleStatus status = RoleStatus.Active) =>
        new(new RoleCode("Admin"), status);

    private static Permission NewPermission(string code) =>
        new(new PermissionCode(code), PermissionStatus.Active);

    [Theory]
    [InlineData(RoleStatus.Active)]
    [InlineData(RoleStatus.Deprecated)]
    public void Constructor_UsesTheExplicitlySuppliedStatus(RoleStatus status)
    {
        var role = new Role(new RoleCode("Accountant"), status);

        Assert.Equal(status, role.Status);
        Assert.Equal("Accountant", role.Code.Value);
    }

    [Fact]
    public void Constructor_GeneratesAVersion7Guid_WhenNoIdIsProvided()
    {
        Assert.Equal('7', NewRole().Id.ToString()[14]);
    }

    [Fact]
    public void Role_CanExist_WithZeroPermissions()
    {
        Assert.Empty(NewRole().Permissions);
    }

    [Fact]
    public void Deprecate_MovesActiveToDeprecated()
    {
        var role = NewRole();

        role.Deprecate();

        Assert.Equal(RoleStatus.Deprecated, role.Status);
    }

    [Fact]
    public void Deprecate_Throws_WhenAlreadyDeprecated()
    {
        var role = NewRole(RoleStatus.Deprecated);

        Assert.Throws<InvalidOperationException>(() => role.Deprecate());
    }

    [Fact]
    public void GrantPermission_AddsAGrantOwnedByThisRole()
    {
        var role = NewRole();
        var permission = NewPermission("Users.Manage");

        var grant = role.GrantPermission(permission);

        Assert.Single(role.Permissions);
        Assert.Equal(role.Id, grant.RoleId);
        Assert.Equal(permission.Id, grant.PermissionId);
        Assert.Equal('7', grant.Id.ToString()[14]);
    }

    [Fact]
    public void GrantPermission_AllowsSeveralDifferentPermissions()
    {
        var role = NewRole();

        role.GrantPermission(NewPermission("Users.Manage"));
        role.GrantPermission(NewPermission("Roles.Manage"));

        Assert.Equal(2, role.Permissions.Count);
    }

    [Fact]
    public void GrantPermission_RejectsADuplicateGrant()
    {
        var role = NewRole();
        var permission = NewPermission("Users.Manage");
        role.GrantPermission(permission);

        Assert.Throws<InvalidOperationException>(() => role.GrantPermission(permission));
        Assert.Single(role.Permissions);
    }

    [Theory]
    [InlineData("Payroll.View")]
    [InlineData("payroll.view")]
    [InlineData("PAYROLL.VIEW")]
    [InlineData("Payroll.view")]
    public void GrantPermission_RejectsPayrollView_CaseInsensitively(string code)
    {
        var role = NewRole();

        Assert.Throws<InvalidOperationException>(() => role.GrantPermission(NewPermission(code)));
        Assert.Empty(role.Permissions);
    }

    [Fact]
    public void GrantPermission_RejectsPayrollView_EvenForAnAdminRole()
    {
        var admin = new Role(new RoleCode("Admin"), RoleStatus.Active);

        Assert.Throws<InvalidOperationException>(() => admin.GrantPermission(NewPermission(PermissionCode.PayrollView)));
    }

    [Fact]
    public void GrantPermission_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => NewRole().GrantPermission(null!));
    }
}
