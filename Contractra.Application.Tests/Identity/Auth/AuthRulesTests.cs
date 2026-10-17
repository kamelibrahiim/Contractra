using Contractra.Application.Identity.Auth;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Auth;

/// <summary>Contract v1.5 §14.1 actor_role_code (as written; OC-6 still open).</summary>
public class ActorRoleCodeRuleTests
{
    private static UserAccess Access(Dictionary<string, string[]> byRole, string[]? direct = null) =>
        new(byRole.Keys.ToList(),
            byRole.Values.SelectMany(p => p).Concat(direct ?? []).Distinct().ToList(),
            byRole.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value));

    [Fact]
    public void TheRoleThroughWhichThePermissionWasHeld()
    {
        var access = Access(new() { ["Accountant"] = ["Apps.Admin.Access"], ["Admin"] = ["Apps.Admin.Access", "Users.Manage"] });
        Assert.Equal("Admin", ActorRoleCodeRule.Resolve(access, "Users.Manage"));
    }

    [Fact]
    public void SeveralGrantingRoles_TheFirstByOrdinalCodeOrder()
    {
        var access = Access(new() { ["OfficeStaff"] = ["Apps.Admin.Access"], ["Admin"] = ["Apps.Admin.Access"], ["Accountant"] = ["Apps.Admin.Access"] });
        Assert.Equal("Accountant", ActorRoleCodeRule.Resolve(access, "Apps.Admin.Access"));
    }

    [Fact]
    public void OrdinalNotCulture_UpperCaseSortsBeforeLowerCase()
    {
        var access = Access(new() { ["admin2"] = [], ["Zeta"] = [] });
        Assert.Equal("Zeta", ActorRoleCodeRule.Resolve(access, null));
    }

    [Fact]
    public void PermissionOnlyFromADirectGrant_TheFirstActiveRole()
    {
        var access = Access(new() { ["OfficeStaff"] = ["Apps.Admin.Access"], ["Accountant"] = ["Apps.Admin.Access"] }, direct: ["Payroll.View"]);
        Assert.Equal("Accountant", ActorRoleCodeRule.Resolve(access, "Payroll.View"));
    }

    [Fact]
    public void NoPermissionRequired_TheFirstActiveRole()
    {
        var access = Access(new() { ["Engineer"] = ["Apps.Employee.Access"], ["Accountant"] = [] });
        Assert.Equal("Accountant", ActorRoleCodeRule.Resolve(access, null));
    }

    [Fact]
    public void PermissionNotHeldAtAll_TheFirstActiveRole()
    {
        var access = Access(new() { ["Engineer"] = ["Apps.Employee.Access"] });
        Assert.Equal("Engineer", ActorRoleCodeRule.Resolve(access, "Apps.Admin.Access"));
    }

    [Fact]
    public void NoActiveRole_Null()
    {
        Assert.Null(ActorRoleCodeRule.Resolve(UserAccess.None, "Users.Manage"));
        Assert.Null(ActorRoleCodeRule.Resolve(Access(new(), direct: ["Payroll.View"]), "Payroll.View"));
    }
}

/// <summary>Contract v1.5 §3.1, §3.6, E15.</summary>
public class AuthSessionPolicyTests
{
    [Fact]
    public void Defaults_AreTheContractValues()
    {
        var policy = AuthSessionPolicy.Default;
        Assert.Equal(TimeSpan.FromHours(8), policy.IdleTimeoutFor(["Engineer"]));
        Assert.Equal(TimeSpan.FromMinutes(60), policy.IdleTimeoutFor(["Accountant"]));
        Assert.Equal(TimeSpan.FromMinutes(60), policy.IdleTimeoutFor(["Admin"]));
        Assert.Equal(TimeSpan.FromMinutes(60), policy.IdleTimeoutFor(["OfficeStaff"]));
        Assert.Equal(TimeSpan.FromMinutes(60), policy.IdleTimeoutFor([]));
        Assert.Equal(TimeSpan.FromMinutes(60), policy.IdleTimeoutFor(["Engineer", "Admin"]));
        Assert.Equal(TimeSpan.FromMinutes(30), AuthSessionPolicy.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(7), AuthSessionPolicy.SessionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), AuthSessionPolicy.RefreshReuseGrace);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(24 * 60 + 1)]
    public void OutOfBounds_IsRejected(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthSessionPolicy(new Dictionary<string, TimeSpan> { ["Engineer"] = TimeSpan.FromMinutes(minutes) }));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(24 * 60)]
    public void Bounds_AreInclusive(int minutes)
    {
        var policy = new AuthSessionPolicy(new Dictionary<string, TimeSpan> { ["Engineer"] = TimeSpan.FromMinutes(minutes) });
        Assert.Equal(TimeSpan.FromMinutes(minutes), policy.IdleTimeoutFor(["Engineer"]));
    }
}

/// <summary>§5.5 + §17 layer 4 and §5.6 + §17 layers 2-3 around the unchanged Slice 4 use cases.</summary>
public class ThrottledFlowsTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeSessionStore _sessions = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeAuditWriter _audit = new();
    private readonly FakeClock _clock = new();
    private readonly FakeAuthThrottle _throttle = new();

    private (User User, AuthSession Session) Seed()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, _hasher.Hash("Old-Passw0rd"), false);
        _users.Seed(user);
        var session = AuthSession.Start(user.Id, ClientApp.Employee, ClientPlatform.Mobile, new byte[32], _clock.UtcNow, _clock.UtcNow.AddDays(7));
        _sessions.Seed(session);
        return (user, session);
    }

    private ThrottledChangePassword ChangePassword() =>
        new(new ChangePasswordUseCase(_users, _sessions, _hasher, _audit, _clock, new FakeUnitOfWork()), _sessions, _throttle, _clock);

    private ChangePasswordAttempt Attempt(User user, AuthSession session, string current, string next) =>
        new(user.Id, session.Id, current, next, "corr-1", AuditSource.EmployeeApp);

    [Fact]
    public async Task ChangePassword_WrongCurrentStaysCounted_EverythingElseIsReleased()
    {
        var (user, session) = Seed();

        await ChangePassword().ExecuteAsync(Attempt(user, session, "wrong", "Brand-New-1"));
        Assert.DoesNotContain("change-password-released", _throttle.Calls);

        await ChangePassword().ExecuteAsync(Attempt(user, session, "Old-Passw0rd", "short"));
        Assert.Single(_throttle.Calls, c => c == "change-password-released");

        var ok = await ChangePassword().ExecuteAsync(Attempt(user, session, "Old-Passw0rd", "Brand-New-1"));
        Assert.True(ok.IsSuccess);
        Assert.Equal(2, _throttle.Calls.Count(c => c == "change-password-released"));
    }

    [Fact]
    public async Task ChangePassword_ThrottledOrUnavailable_NeverReachesTheUseCase()
    {
        var (user, session) = Seed();
        _throttle.NextStatus = ThrottleStatus.Throttled;
        Assert.Equal(AuthFailureKind.Throttled, (await ChangePassword().ExecuteAsync(Attempt(user, session, "Old-Passw0rd", "Brand-New-1"))).Failure!.Kind);
        _throttle.NextStatus = ThrottleStatus.Unavailable;
        Assert.Equal(AuthFailureKind.ThrottleUnavailable, (await ChangePassword().ExecuteAsync(Attempt(user, session, "Old-Passw0rd", "Brand-New-1"))).Failure!.Kind);
        Assert.Empty(_audit.Staged);
        Assert.Equal(_hasher.Hash("Old-Passw0rd"), user.PasswordHash);
    }

    [Fact]
    public async Task ChangePassword_FromARevokedSession_IsUnauthenticated()
    {
        var (user, session) = Seed();
        session.Revoke(SessionRevokeReason.Logout, _clock.UtcNow);
        var outcome = await ChangePassword().ExecuteAsync(Attempt(user, session, "Old-Passw0rd", "Brand-New-1"));
        Assert.Equal(AuthErrorCodes.Unauthenticated, outcome.Failure!.Code);
        Assert.Empty(_audit.Staged);
    }

    [Fact]
    public async Task ResetCompletion_ThrottlesFirst_AndReleasesOnlyOnSuccess()
    {
        var tickets = new FakePasswordResetTicketStore();
        var codes = new FakeResetCodeGenerator();
        var completion = new ThrottledPasswordResetCompletion(
            new CompletePasswordResetUseCase(_users, _sessions, tickets, codes, _hasher, _audit, _clock, new FakeUnitOfWork()), _throttle, _clock);

        var failed = await completion.ExecuteAsync(new PasswordResetCompletionAttempt("nobody", "AAAA", "Chosen-Passw0rd", "203.0.113.1", "corr-1", AuditSource.Web));
        Assert.Equal(AuthErrorCodes.ResetCodeInvalid, failed.Failure!.Code);
        Assert.Equal(["admit-credential:" + ThrottleKeys.ToHex(ThrottleKeys.IdentifierKey("nobody")) + ":203.0.113.1"], _throttle.Calls);

        _throttle.NextStatus = ThrottleStatus.Throttled;
        var throttled = await completion.ExecuteAsync(new PasswordResetCompletionAttempt("nobody", null, null, "203.0.113.1", "corr-1", AuditSource.Web));
        Assert.Equal(AuthFailureKind.Throttled, throttled.Failure!.Kind);
    }
}
