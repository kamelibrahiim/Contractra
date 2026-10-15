using System.Reflection;
using Contractra.Domain.Identity;
using Contractra.Domain.Parties;
using static Contractra.Domain.Tests.Identity.TestUsers;

namespace Contractra.Domain.Tests.Identity;

/// <summary>
/// security_version semantics (auth contract v1.5 §3.5, plan E7) and the
/// aggregate-only paths for ending roles and revoking grants (R34).
/// </summary>
public class UserSecurityVersionTests
{
    private static Role NewRole(string code = "Engineer") => new(new RoleCode(code), RoleStatus.Active);

    private static Permission NewPermission(string code = "Reports.Monthly.View") =>
        new(new PermissionCode(code), PermissionStatus.Active);

    [Fact]
    public void AssignRole_BumpsTheVersionOnce_IncludingDuringCreation()
    {
        var user = Active();

        user.AssignRole(NewRole(), T0, T0);

        Assert.Equal(2, user.SecurityVersion);
    }

    [Fact]
    public void EndRole_EndsTheOpenAssignment_AndBumpsTheVersionOnce()
    {
        var user = Active();
        var role = NewRole();
        var userRole = user.AssignRole(role, T0, T0);

        user.EndRole(role, T0.AddDays(1), T0.AddDays(1));

        Assert.Equal(T0.AddDays(1), userRole.EndedAt);
        Assert.Equal(3, user.SecurityVersion);
    }

    [Fact]
    public void EndRole_EndsOnlyTheOpenAssignment_WhenTheRoleHasHistory()
    {
        var user = Active();
        var role = NewRole();
        var first = user.AssignRole(role, T0, T0);
        user.EndRole(role, T0.AddDays(1), T0.AddDays(1));
        var second = user.AssignRole(role, T0.AddDays(2), T0.AddDays(2));

        user.EndRole(role, T0.AddDays(3), T0.AddDays(3));

        Assert.Equal(T0.AddDays(1), first.EndedAt);
        Assert.Equal(T0.AddDays(3), second.EndedAt);
    }

    [Fact]
    public void EndRole_Throws_WhenTheRoleHasNoOpenAssignment_AndChangesNothing()
    {
        var user = Active();

        Assert.Throws<InvalidOperationException>(() => user.EndRole(NewRole(), T0, T0));
        Assert.Equal(1, user.SecurityVersion);
    }

    [Fact]
    public void EndRole_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Active().EndRole(null!, T0, T0));
    }

    [Fact]
    public void EndRole_WithAnInvalidTimestamp_LeavesTheAssignmentAndTheVersionUnchanged()
    {
        var user = Active();
        var role = NewRole();
        var userRole = user.AssignRole(role, T0, T0);

        Assert.Throws<ArgumentException>(() => user.EndRole(role, T0.AddMicroseconds(-1), T0));
        Assert.Null(userRole.EndedAt);
        Assert.Equal(2, user.SecurityVersion);
    }

    [Fact]
    public void GrantPermission_BumpsTheVersionOnce()
    {
        var user = Active();

        user.GrantPermission(NewPermission(), T0, T0);

        Assert.Equal(2, user.SecurityVersion);
    }

    [Fact]
    public void RevokePermission_RevokesTheActiveGrant_AndBumpsTheVersionOnce()
    {
        var user = Active();
        var permission = NewPermission();
        var grant = user.GrantPermission(permission, T0, T0);

        user.RevokePermission(permission, T0.AddDays(1), T0.AddDays(1));

        Assert.Equal(T0.AddDays(1), grant.RevokedAt);
        Assert.Equal(3, user.SecurityVersion);
    }

    [Fact]
    public void RevokePermission_Throws_WhenThereIsNoActiveGrant_AndChangesNothing()
    {
        var user = Active();

        Assert.Throws<InvalidOperationException>(() => user.RevokePermission(NewPermission(), T0, T0));
        Assert.Equal(1, user.SecurityVersion);
    }

    [Fact]
    public void RevokePermission_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Active().RevokePermission(null!, T0, T0));
    }

    [Fact]
    public void Disable_AndReactivate_EachBumpTheVersionOnce()
    {
        var user = Active();

        user.Disable();
        Assert.Equal(2, user.SecurityVersion);

        user.Reactivate();
        Assert.Equal(3, user.SecurityVersion);
    }

    [Fact]
    public void RejectedStatusTransitions_LeaveTheVersionUnchanged()
    {
        var active = Active();
        var disabled = WithStatus(UserStatus.Disabled);

        Assert.Throws<InvalidOperationException>(() => active.Reactivate());
        Assert.Throws<InvalidOperationException>(() => disabled.Disable());

        Assert.Equal(1, active.SecurityVersion);
        Assert.Equal(1, disabled.SecurityVersion);
    }

    [Fact]
    public void RejectedAssignmentsAndGrants_LeaveTheVersionUnchanged()
    {
        var user = Active();
        var role = NewRole();
        var permission = NewPermission();
        user.AssignRole(role, T0, T0);
        user.GrantPermission(permission, T0, T0);
        var cairo = new DateTimeOffset(2026, 10, 2, 11, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<InvalidOperationException>(() => user.AssignRole(role, T0.AddDays(1), T0.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => user.GrantPermission(permission, T0.AddDays(1), T0.AddDays(1)));
        Assert.Throws<ArgumentException>(() => user.AssignRole(NewRole("OfficeStaff"), cairo, cairo));
        Assert.Throws<ArgumentException>(() => user.GrantPermission(NewPermission("Expenses.Create"), cairo, cairo));

        Assert.Equal(3, user.SecurityVersion);
        Assert.Single(user.Roles);
        Assert.Single(user.Permissions);
    }

    [Fact]
    public void LinkToParty_IsNotASecurityStateChange()
    {
        var user = Active();

        user.LinkToParty(new Party("م. أحمد", PartyStatus.Active));

        Assert.Equal(1, user.SecurityVersion);
    }

    [Fact]
    public void UserRoleEnd_AndUserPermissionRevoke_AreNotPublic()
    {
        // R34: ending a role or revoking a grant must go through the User
        // aggregate so the security version is always bumped.
        Assert.Null(typeof(UserRole).GetMethod("End", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(UserPermission).GetMethod("Revoke", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(UserRole).GetMethod("End", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(typeof(UserPermission).GetMethod("Revoke", BindingFlags.NonPublic | BindingFlags.Instance));
    }

    [Fact]
    public void SecurityState_HasNoPublicSetters()
    {
        foreach (var name in new[]
                 {
                     nameof(User.PasswordHash), nameof(User.MustChangePassword), nameof(User.TemporaryPasswordExpiresAt),
                     nameof(User.SecurityVersion), nameof(User.IsBootstrapAdmin),
                 })
        {
            var setter = typeof(User).GetProperty(name)!.GetSetMethod(nonPublic: false);
            Assert.Null(setter);
        }
    }
}
