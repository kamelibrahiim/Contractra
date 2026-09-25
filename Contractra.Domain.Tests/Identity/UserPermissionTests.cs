using System.Reflection;
using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

public class UserPermissionTests
{
    // PostgreSQL timestamptz stores microseconds, so boundaries are tested at
    // microsecond steps rather than .NET's 100-ns ticks.
    private static readonly DateTimeOffset GrantedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static User NewUser() => new(new LoginIdentifier("Accountant.User01"), UserStatus.Active);

    private static Permission NewPermission(string code = "Reports.Monthly.View") =>
        new(new PermissionCode(code), PermissionStatus.Active);

    private static UserPermission GrantNow(User user, Permission permission, DateTimeOffset at) =>
        user.GrantPermission(permission, at, at);

    [Fact]
    public void User_GrantPermission_CreatesActiveGrant()
    {
        var user = NewUser();
        var permission = NewPermission();

        var grant = user.GrantPermission(permission, GrantedAt, GrantedAt.AddMinutes(1));

        Assert.Same(grant, Assert.Single(user.Permissions));
        Assert.Equal(user.Id, grant.UserId);
        Assert.Equal(permission.Id, grant.PermissionId);
        Assert.Equal(GrantedAt, grant.GrantedAt);
        Assert.Null(grant.RevokedAt);
        Assert.Equal('7', grant.Id.ToString()[14]);
    }

    [Theory]
    [InlineData("Payroll.View")]
    [InlineData("payroll.view")]
    [InlineData("PAYROLL.VIEW")]
    public void User_GrantPermission_AllowsPayrollView_TheOnlySanctionedPath(string code)
    {
        // BR-235 / FR-AUTH-006: Payroll.View is never granted through a role, and
        // is granted explicitly per user - this path must stay open.
        var user = NewUser();

        var grant = GrantNow(user, NewPermission(code), GrantedAt);

        Assert.True(grant.IsActiveAt(GrantedAt));
        Assert.Single(user.Permissions);
    }

    [Fact]
    public void PayrollView_IsRejectedThroughARole_ButAcceptedPerUser()
    {
        var payrollView = NewPermission(PermissionCode.PayrollView);

        Assert.Throws<InvalidOperationException>(() =>
            new Role(new RoleCode("Admin"), RoleStatus.Active).GrantPermission(payrollView));
        Assert.NotNull(GrantNow(NewUser(), payrollView, GrantedAt));
    }

    [Fact]
    public void User_GrantPermission_RejectsNullPermission()
    {
        Assert.Throws<ArgumentNullException>(() => NewUser().GrantPermission(null!, GrantedAt, GrantedAt));
    }

    [Fact]
    public void User_GrantPermission_RejectsANonUtcTimestamp()
    {
        var user = NewUser();
        var cairoTime = new DateTimeOffset(2026, 9, 1, 11, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(() => user.GrantPermission(NewPermission(), cairoTime, cairoTime));
        Assert.Empty(user.Permissions);
    }

    [Fact]
    public void User_GrantPermission_Throws_WhenGrantedAtIsAfterAsOf()
    {
        var user = NewUser();

        Assert.Throws<ArgumentException>(() =>
            user.GrantPermission(NewPermission(), GrantedAt.AddMicroseconds(1), GrantedAt));
        Assert.Empty(user.Permissions);
    }

    [Fact]
    public void User_CanHaveZeroDirectPermissions()
    {
        Assert.Empty(NewUser().Permissions);
    }

    [Fact]
    public void User_CanHold_DifferentPermissionsAtTheSameTime()
    {
        var user = NewUser();

        GrantNow(user, NewPermission("Reports.Monthly.View"), GrantedAt);
        GrantNow(user, NewPermission(PermissionCode.PayrollView), GrantedAt);

        Assert.Equal(2, user.Permissions.Count);
    }

    [Fact]
    public void User_GrantPermission_Throws_WhenTheSamePermissionIsStillGranted()
    {
        var user = NewUser();
        var permission = NewPermission();
        GrantNow(user, permission, GrantedAt);

        Assert.Throws<InvalidOperationException>(() => GrantNow(user, permission, GrantedAt.AddDays(1)));
        Assert.Single(user.Permissions);
    }

    [Fact]
    public void User_GrantPermission_Throws_WhenABackdatedRegrantOverlapsARevokedOne()
    {
        var user = NewUser();
        var permission = NewPermission();
        GrantNow(user, permission, GrantedAt).Revoke(GrantedAt.AddDays(10), GrantedAt.AddDays(10));

        Assert.Throws<InvalidOperationException>(() =>
            user.GrantPermission(permission, GrantedAt.AddDays(5), GrantedAt.AddDays(12)));
        Assert.Single(user.Permissions);
    }

    [Fact]
    public void User_CanHave_MultipleHistoricalGrants_ForSamePermission()
    {
        var user = NewUser();
        var permission = NewPermission(PermissionCode.PayrollView);
        var first = GrantNow(user, permission, GrantedAt);
        first.Revoke(GrantedAt.AddDays(10), GrantedAt.AddDays(10));

        var second = GrantNow(user, permission, GrantedAt.AddDays(20));

        Assert.Equal(2, user.Permissions.Count);
        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.IsActiveAt(GrantedAt.AddDays(25)));
        Assert.True(second.IsActiveAt(GrantedAt.AddDays(25)));
    }

    [Fact]
    public void User_CanRegrantAPermission_AtTheExactInstantThePreviousGrantWasRevoked()
    {
        var user = NewUser();
        var permission = NewPermission();
        var revokedAt = GrantedAt.AddDays(10);
        GrantNow(user, permission, GrantedAt).Revoke(revokedAt, revokedAt);

        var second = GrantNow(user, permission, revokedAt);

        Assert.Same(second, Assert.Single(user.Permissions, p => p.IsActiveAt(revokedAt)));
    }

    [Fact]
    public void IsActiveAt_IsFalse_BeforeTheGrantInstant()
    {
        var grant = GrantNow(NewUser(), NewPermission(), GrantedAt);

        Assert.False(grant.IsActiveAt(GrantedAt.AddMicroseconds(-1)));
        Assert.True(grant.IsActiveAt(GrantedAt));
    }

    [Fact]
    public void IsActiveAt_StopsExactlyAtTheRevocationInstant()
    {
        var grant = GrantNow(NewUser(), NewPermission(), GrantedAt);
        var revokedAt = GrantedAt.AddHours(5);
        grant.Revoke(revokedAt, revokedAt);

        Assert.True(grant.IsActiveAt(revokedAt.AddMicroseconds(-1)));
        Assert.False(grant.IsActiveAt(revokedAt));
    }

    [Fact]
    public void Revoke_SetsRevokedAt()
    {
        var grant = GrantNow(NewUser(), NewPermission(), GrantedAt);
        var revokedAt = GrantedAt.AddDays(3);

        grant.Revoke(revokedAt, revokedAt.AddMinutes(1));

        Assert.Equal(revokedAt, grant.RevokedAt);
    }

    [Fact]
    public void Revoke_Throws_WhenRevokedAtIsAfterAsOf()
    {
        var grant = GrantNow(NewUser(), NewPermission(), GrantedAt);
        var asOf = GrantedAt.AddDays(3);

        Assert.Throws<ArgumentException>(() => grant.Revoke(asOf.AddMicroseconds(1), asOf));
        Assert.Null(grant.RevokedAt);
    }

    [Fact]
    public void Revoke_Throws_WhenRevokedAtIsBeforeGrantedAt()
    {
        var grant = GrantNow(NewUser(), NewPermission(), GrantedAt);

        Assert.Throws<ArgumentException>(() => grant.Revoke(GrantedAt.AddMicroseconds(-1), GrantedAt));
        Assert.Null(grant.RevokedAt);
    }

    [Fact]
    public void Revoke_Throws_WhenAlreadyRevoked()
    {
        var grant = GrantNow(NewUser(), NewPermission(), GrantedAt);
        var firstRevoke = GrantedAt.AddDays(1);
        grant.Revoke(firstRevoke, firstRevoke);

        Assert.Throws<InvalidOperationException>(() => grant.Revoke(firstRevoke.AddDays(1), firstRevoke.AddDays(1)));
        Assert.Equal(firstRevoke, grant.RevokedAt);
    }

    [Fact]
    public void Revoke_RejectsANonUtcTimestamp()
    {
        var grant = GrantNow(NewUser(), NewPermission(), GrantedAt);
        var cairoTime = new DateTimeOffset(2026, 9, 2, 11, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(() => grant.Revoke(cairoTime, cairoTime));
        Assert.Null(grant.RevokedAt);
    }

    [Fact]
    public void UserPermission_HasNoPublicConstructor_SoItIsCreatedOnlyThroughUserGrantPermission()
    {
        Assert.Empty(typeof(UserPermission).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void UserPermission_HasNoDeleteOrRemoveMethod()
    {
        var methodNames = typeof(UserPermission).GetMethods().Select(m => m.Name);

        Assert.DoesNotContain(methodNames, name =>
            name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }
}
