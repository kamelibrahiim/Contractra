using System.Reflection;
using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

public class UserRoleTests
{
    // PostgreSQL timestamptz stores microseconds, so boundaries are tested at
    // microsecond steps rather than .NET's 100-ns ticks.
    private static readonly DateTimeOffset AssignedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static User NewUser() => new(new LoginIdentifier("Engineer.User01"), UserStatus.Active);

    private static Role NewRole(string code = "Engineer") => new(new RoleCode(code), RoleStatus.Active);

    private static UserRole AssignNow(User user, Role role, DateTimeOffset at) => user.AssignRole(role, at, at);

    [Fact]
    public void User_AssignRole_CreatesActiveUserRole()
    {
        var user = NewUser();
        var role = NewRole();

        var userRole = user.AssignRole(role, AssignedAt, AssignedAt.AddMinutes(1));

        Assert.Single(user.Roles);
        Assert.Same(userRole, user.Roles.Single());
        Assert.Equal(user.Id, userRole.UserId);
        Assert.Equal(role.Id, userRole.RoleId);
        Assert.Equal(AssignedAt, userRole.AssignedAt);
        Assert.Null(userRole.EndedAt);
        Assert.Equal('7', userRole.Id.ToString()[14]);
    }

    [Fact]
    public void User_AssignRole_RejectsNullRole()
    {
        Assert.Throws<ArgumentNullException>(() => NewUser().AssignRole(null!, AssignedAt, AssignedAt));
    }

    [Fact]
    public void User_AssignRole_RejectsANonUtcTimestamp()
    {
        var user = NewUser();
        var cairoTime = new DateTimeOffset(2026, 9, 1, 11, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(() => user.AssignRole(NewRole(), cairoTime, cairoTime));
        Assert.Empty(user.Roles);
    }

    [Fact]
    public void User_AssignRole_Throws_WhenAssignedAtIsAfterAsOf()
    {
        var user = NewUser();

        Assert.Throws<ArgumentException>(() => user.AssignRole(NewRole(), AssignedAt.AddMicroseconds(1), AssignedAt));
        Assert.Empty(user.Roles);
    }

    [Fact]
    public void User_CanHaveZeroRoles()
    {
        Assert.Empty(NewUser().Roles);
    }

    [Fact]
    public void User_CanHold_DifferentRolesAtTheSameTime()
    {
        var user = NewUser();

        AssignNow(user, NewRole("Engineer"), AssignedAt);
        AssignNow(user, NewRole("OfficeStaff"), AssignedAt);

        Assert.Equal(2, user.Roles.Count);
    }

    [Fact]
    public void User_AssignRole_Throws_WhenTheSameRoleIsStillOpen()
    {
        var user = NewUser();
        var role = NewRole();
        AssignNow(user, role, AssignedAt);

        Assert.Throws<InvalidOperationException>(() => AssignNow(user, role, AssignedAt.AddDays(1)));
        Assert.Single(user.Roles);
    }

    [Fact]
    public void User_AssignRole_Throws_WhenABackdatedReassignmentOverlapsAnEndedOne()
    {
        var user = NewUser();
        var role = NewRole();
        var first = AssignNow(user, role, AssignedAt);
        first.End(AssignedAt.AddDays(10), AssignedAt.AddDays(10));

        Assert.Throws<InvalidOperationException>(() =>
            user.AssignRole(role, AssignedAt.AddDays(5), AssignedAt.AddDays(12)));
        Assert.Single(user.Roles);
    }

    [Fact]
    public void User_CanHave_MultipleHistoricalUserRoles_ForSameRole()
    {
        var user = NewUser();
        var role = NewRole();
        var first = AssignNow(user, role, AssignedAt);
        first.End(AssignedAt.AddDays(10), AssignedAt.AddDays(10));

        var second = AssignNow(user, role, AssignedAt.AddDays(20));

        Assert.Equal(2, user.Roles.Count);
        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.IsActiveAt(AssignedAt.AddDays(25)));
        Assert.True(second.IsActiveAt(AssignedAt.AddDays(25)));
    }

    [Fact]
    public void User_CanReassignARole_AtTheExactInstantThePreviousOneEnded()
    {
        var user = NewUser();
        var role = NewRole();
        var endedAt = AssignedAt.AddDays(10);
        AssignNow(user, role, AssignedAt).End(endedAt, endedAt);

        var second = AssignNow(user, role, endedAt);

        Assert.Equal(2, user.Roles.Count);
        Assert.Single(user.Roles, r => r.IsActiveAt(endedAt));
        Assert.Same(second, user.Roles.Single(r => r.IsActiveAt(endedAt)));
    }

    [Fact]
    public void IsActiveAt_IsFalse_BeforeTheAssignmentInstant()
    {
        var userRole = AssignNow(NewUser(), NewRole(), AssignedAt);

        Assert.False(userRole.IsActiveAt(AssignedAt.AddMicroseconds(-1)));
    }

    [Fact]
    public void IsActiveAt_IsTrue_FromTheAssignmentInstant_WhileNotEnded()
    {
        var userRole = AssignNow(NewUser(), NewRole(), AssignedAt);

        Assert.True(userRole.IsActiveAt(AssignedAt));
        Assert.True(userRole.IsActiveAt(AssignedAt.AddYears(5)));
    }

    [Fact]
    public void IsActiveAt_StopsExactlyAtTheEndInstant()
    {
        var userRole = AssignNow(NewUser(), NewRole(), AssignedAt);
        var endedAt = AssignedAt.AddHours(5);
        userRole.End(endedAt, endedAt);

        Assert.True(userRole.IsActiveAt(endedAt.AddMicroseconds(-1)));
        Assert.False(userRole.IsActiveAt(endedAt));
    }

    [Fact]
    public void End_SetsEndedAt()
    {
        var userRole = AssignNow(NewUser(), NewRole(), AssignedAt);
        var endedAt = AssignedAt.AddDays(3);

        userRole.End(endedAt, endedAt.AddMinutes(1));

        Assert.Equal(endedAt, userRole.EndedAt);
    }

    [Fact]
    public void End_Throws_WhenEndedAtIsAfterAsOf()
    {
        var userRole = AssignNow(NewUser(), NewRole(), AssignedAt);
        var asOf = AssignedAt.AddDays(3);

        Assert.Throws<ArgumentException>(() => userRole.End(asOf.AddMicroseconds(1), asOf));
        Assert.Null(userRole.EndedAt);
    }

    [Fact]
    public void End_Throws_WhenEndedAtIsBeforeAssignedAt()
    {
        var userRole = AssignNow(NewUser(), NewRole(), AssignedAt);

        Assert.Throws<ArgumentException>(() => userRole.End(AssignedAt.AddMicroseconds(-1), AssignedAt));
        Assert.Null(userRole.EndedAt);
    }

    [Fact]
    public void End_Throws_WhenAlreadyEnded()
    {
        var userRole = AssignNow(NewUser(), NewRole(), AssignedAt);
        var firstEnd = AssignedAt.AddDays(1);
        userRole.End(firstEnd, firstEnd);

        Assert.Throws<InvalidOperationException>(() => userRole.End(firstEnd.AddDays(1), firstEnd.AddDays(1)));
        Assert.Equal(firstEnd, userRole.EndedAt);
    }

    [Fact]
    public void End_RejectsANonUtcTimestamp()
    {
        var userRole = AssignNow(NewUser(), NewRole(), AssignedAt);
        var cairoTime = new DateTimeOffset(2026, 9, 2, 11, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(() => userRole.End(cairoTime, cairoTime));
        Assert.Null(userRole.EndedAt);
    }

    [Fact]
    public void UserRole_HasNoPublicConstructor_SoItIsCreatedOnlyThroughUserAssignRole()
    {
        Assert.Empty(typeof(UserRole).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void UserRole_HasNoDeleteOrRemoveMethod()
    {
        var methodNames = typeof(UserRole).GetMethods().Select(m => m.Name)
            .Concat(typeof(User).GetMethods().Select(m => m.Name));

        Assert.DoesNotContain(methodNames, name =>
            name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }
}
