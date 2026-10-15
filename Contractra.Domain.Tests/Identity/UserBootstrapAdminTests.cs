using Contractra.Domain.Identity;
using static Contractra.Domain.Tests.Identity.TestUsers;

namespace Contractra.Domain.Tests.Identity;

/// <summary>
/// The bootstrap admin (auth contract v1.5 §11; owner decisions C3, R19, R26–R28;
/// plan E7 — the aggregate is a second line of defence behind the use cases).
/// </summary>
public class UserBootstrapAdminTests
{
    [Fact]
    public void CreateBootstrapAdmin_CreatesAnActiveAdmin_WithPayrollViewGrantedDirectly()
    {
        var adminRole = AdminRole();
        var payrollView = PayrollView();

        var user = User.CreateBootstrapAdmin(new LoginIdentifier("bootstrap.admin"), Hash, adminRole, payrollView, T0, T0);

        Assert.True(user.IsBootstrapAdmin);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.False(user.MustChangePassword);
        Assert.Null(user.TemporaryPasswordExpiresAt);
        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(adminRole.Id, Assert.Single(user.Roles).RoleId);
        Assert.Equal(payrollView.Id, Assert.Single(user.Permissions).PermissionId);
        Assert.True(user.Permissions.Single().IsActiveAt(T0));
        Assert.Equal(3, user.SecurityVersion);
    }

    [Fact]
    public void OrdinaryUsers_AreNeverBootstrapAdmins()
    {
        Assert.False(Active().IsBootstrapAdmin);
        Assert.False(WithTemporaryPassword().IsBootstrapAdmin);
    }

    [Fact]
    public void CreateBootstrapAdmin_RejectsARoleOtherThanAdmin()
    {
        Assert.Throws<ArgumentException>(() => User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), Hash, new Role(new RoleCode("Accountant"), RoleStatus.Active), PayrollView(), T0, T0));
    }

    [Fact]
    public void CreateBootstrapAdmin_RejectsADeprecatedAdminRole()
    {
        var role = AdminRole();
        role.Deprecate();

        Assert.Throws<ArgumentException>(() => User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), Hash, role, PayrollView(), T0, T0));
    }

    [Fact]
    public void CreateBootstrapAdmin_RejectsAPermissionOtherThanPayrollView()
    {
        Assert.Throws<ArgumentException>(() => User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), Hash, AdminRole(), new Permission(new PermissionCode("Users.Manage"), PermissionStatus.Active), T0, T0));
    }

    [Fact]
    public void CreateBootstrapAdmin_RejectsTheUnusableMarker()
    {
        Assert.Throws<ArgumentException>(() => User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), User.UnusablePasswordHash, AdminRole(), PayrollView(), T0, T0));
    }

    [Fact]
    public void CreateBootstrapAdmin_RejectsNullRoleOrPermission()
    {
        Assert.Throws<ArgumentNullException>(() => User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), Hash, null!, PayrollView(), T0, T0));
        Assert.Throws<ArgumentNullException>(() => User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), Hash, AdminRole(), null!, T0, T0));
    }

    public static TheoryData<string> ProtectedMutations => new()
    {
        "Disable", "Reactivate", "AssignRole", "EndRole", "GrantPermission", "RevokePermission",
        "BeginPasswordReset", "ReissueTemporaryPassword",
    };

    [Theory]
    [MemberData(nameof(ProtectedMutations))]
    public void ProtectedMutations_AreRefused_AndChangeNothing(string mutation)
    {
        var user = BootstrapAdmin();
        var adminRoleId = user.Roles.Single().RoleId;
        var adminRole = new Role(adminRoleId, new RoleCode("Admin"), RoleStatus.Active);
        var payrollViewId = user.Permissions.Single().PermissionId;
        var payrollView = new Permission(payrollViewId, new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active);
        var later = T0.AddDays(1);

        Action act = mutation switch
        {
            "Disable" => () => user.Disable(),
            "Reactivate" => () => user.Reactivate(),
            "AssignRole" => () => user.AssignRole(new Role(new RoleCode("Accountant"), RoleStatus.Active), later, later),
            "EndRole" => () => user.EndRole(adminRole, later, later),
            "GrantPermission" => () => user.GrantPermission(new Permission(new PermissionCode("Reports.Monthly.View"), PermissionStatus.Active), later, later),
            "RevokePermission" => () => user.RevokePermission(payrollView, later, later),
            "BeginPasswordReset" => () => user.BeginPasswordReset(),
            "ReissueTemporaryPassword" => () => user.ReissueTemporaryPassword(OtherHash, later.AddDays(3), later),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };

        Assert.Throws<InvalidOperationException>(act);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(3, user.SecurityVersion);
        Assert.Null(Assert.Single(user.Roles).EndedAt);
        Assert.Null(Assert.Single(user.Permissions).RevokedAt);
    }

    [Fact]
    public void TheBootstrapAdmin_MayChangeItsOwnPassword_AndIsNeverForcedToChangeIt()
    {
        var user = BootstrapAdmin();

        user.ChangePassword(OtherHash);

        Assert.Equal(OtherHash, user.PasswordHash);
        Assert.False(user.MustChangePassword);
        Assert.True(user.IsBootstrapAdmin);
        Assert.Equal(4, user.SecurityVersion);
    }
}
