using Contractra.Application.Identity.Auth;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Auth;

/// <summary>Auth contract v1.5 §3.3 (rotation, R35 grace), §3.6 (idle, 7 days), §4 (app access at refresh), §5.2, §5.3, §8.</summary>
public class RefreshAndLogoutUseCaseTests
{
    private readonly FakeAuthSessionRepository _sessions = new();
    private readonly FakeSessionStore _sessionStore = new();
    private readonly FakeUserRepository _users = new();
    private readonly FakeUserAccessReader _access = new();
    private readonly FakeRefreshTokenFactory _refreshTokens = new();
    private readonly FakeAccessTokenIssuer _accessTokens = new();
    private readonly FakeClock _clock = new();
    private FakeUnitOfWork _unitOfWork = new();

    private RefreshUseCase CreateRefresh()
    {
        _unitOfWork = new FakeUnitOfWork();
        return new RefreshUseCase(_sessions, _sessionStore, _users, _access, _refreshTokens, _accessTokens, AuthSessionPolicy.Default, _clock, _unitOfWork);
    }

    private (User User, AuthSession Session, string Token) Start(string[] roles, ClientApp app = ClientApp.Employee, ClientPlatform platform = ClientPlatform.Mobile)
    {
        var user = new User(new LoginIdentifier($"u{Guid.NewGuid():N}"), UserStatus.Active, "hash:x", false);
        _users.Seed(user);
        _access.Set(user.Id, roles, new()
        {
            ["Engineer"] = ["Apps.Employee.Access"],
            ["Admin"] = ["Apps.Admin.Access", "Users.Manage"],
            ["OfficeStaff"] = ["Apps.Admin.Access"],
        });
        var token = _refreshTokens.Create();
        var session = AuthSession.Start(user.Id, app, platform, token.Hash, _clock.UtcNow, _clock.UtcNow.AddDays(7));
        _sessions.Add(session);
        _sessionStore.Seed(session);
        return (user, session, token.Token);
    }

    [Fact]
    public async Task Rotation_ConsumesThePresentedToken_AndIssuesTheNext()
    {
        var (_, session, token) = Start(["Engineer"]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(5);

        var outcome = await CreateRefresh().ExecuteAsync(token);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.Value!.RefreshToken);
        Assert.Equal(_clock.UtcNow, session.LastRefreshedAt);
        Assert.True(_unitOfWork.Committed);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public async Task ReuseGrace_IsExactly30Seconds(int secondsLater, bool grace)
    {
        var (_, session, token) = Start(["Engineer"]);
        Assert.True((await CreateRefresh().ExecuteAsync(token)).IsSuccess);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(secondsLater);

        var outcome = await CreateRefresh().ExecuteAsync(token);

        if (grace)
        {
            Assert.True(outcome.IsSuccess);
            Assert.Null(outcome.Value!.RefreshToken);
            Assert.Null(session.RevokedAt);
        }
        else
        {
            Assert.Equal(AuthErrorCodes.RefreshInvalid, outcome.Failure!.Code);
            Assert.Equal(SessionRevokeReason.RefreshReuse, session.RevokeReason);
        }
    }

    [Theory]
    [InlineData(new[] { "Engineer" }, 480, true)]
    [InlineData(new[] { "Engineer" }, 481, false)]
    [InlineData(new[] { "Admin" }, 60, true)]
    [InlineData(new[] { "Admin" }, 61, false)]
    [InlineData(new[] { "Engineer", "Admin" }, 61, false)]
    public async Task IdleTimeout_TheShortestAmongActiveRoles(string[] roles, int minutesIdle, bool active)
    {
        var app = roles.Contains("Engineer") ? ClientApp.Employee : ClientApp.Admin;
        var (_, session, token) = Start(roles, app);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(minutesIdle);

        var outcome = await CreateRefresh().ExecuteAsync(token);

        Assert.Equal(active, outcome.IsSuccess);
        if (!active)
        {
            Assert.Equal(AuthErrorCodes.SessionExpired, outcome.Failure!.Code);
            Assert.Equal(SessionRevokeReason.Idle, session.RevokeReason);
        }
    }

    [Fact]
    public async Task AbsoluteExpiry_IsSessionExpired_WithoutARevocation()
    {
        var (_, session, token) = Start(["Engineer"]);
        _clock.UtcNow = session.AbsoluteExpiresAt;

        var outcome = await CreateRefresh().ExecuteAsync(token);

        Assert.Equal(AuthErrorCodes.SessionExpired, outcome.Failure!.Code);
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public async Task LostAppPermission_RevokesAsAppAccessLost()
    {
        var (user, session, token) = Start(["Engineer"]);
        _access.Set(user.Id, []);

        var outcome = await CreateRefresh().ExecuteAsync(token);

        Assert.Equal(AuthErrorCodes.RefreshInvalid, outcome.Failure!.Code);
        Assert.Equal(SessionRevokeReason.AppAccessLost, session.RevokeReason);
    }

    [Fact]
    public async Task UnknownRevokedOrMissingToken_IsRefreshInvalid()
    {
        var (_, session, token) = Start(["Engineer"]);
        Assert.Equal(AuthErrorCodes.RefreshInvalid, (await CreateRefresh().ExecuteAsync("unknown")).Failure!.Code);
        Assert.Equal(AuthErrorCodes.RefreshInvalid, (await CreateRefresh().ExecuteAsync(null)).Failure!.Code);
        session.Revoke(SessionRevokeReason.Logout, _clock.UtcNow);
        Assert.Equal(AuthErrorCodes.RefreshInvalid, (await CreateRefresh().ExecuteAsync(token)).Failure!.Code);
    }

    [Fact]
    public async Task NewAccessToken_CarriesTheCurrentClaimsAndTheStoredPlatform()
    {
        var (user, _, token) = Start(["OfficeStaff"], ClientApp.Admin, ClientPlatform.Web);
        _access.Set(user.Id, ["Admin", "OfficeStaff"], new() { ["Admin"] = ["Apps.Admin.Access", "Users.Manage"], ["OfficeStaff"] = ["Apps.Admin.Access"] });

        var outcome = await CreateRefresh().ExecuteAsync(token);

        Assert.Equal(ClientPlatform.Web, outcome.Value!.Platform);
        var subject = _accessTokens.Issued.Last();
        Assert.Equal(["Admin", "OfficeStaff"], subject.Roles);
        Assert.Contains("Users.Manage", subject.Permissions);
        Assert.Equal(ClientApp.Admin, subject.App);
    }

    [Fact]
    public async Task RotateAfterPasswordChange_ConsumesTheLatestUnconsumedToken()
    {
        var (user, session, token) = Start(["Engineer"]);

        var outcome = await CreateRefresh().RotateAfterPasswordChangeAsync(user.Id, session.Id);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, session.RefreshTokens.Count);
        Assert.Single(session.RefreshTokens, t => t.ConsumedAt is null);
        // The old token is now consumed: within 30 s a refresh with it is the grace.
        var grace = await CreateRefresh().ExecuteAsync(token);
        Assert.Null(grace.Value!.RefreshToken);
    }

    [Fact]
    public async Task RotateAfterPasswordChange_RefusesARevokedSession()
    {
        var (user, session, _) = Start(["Engineer"]);
        session.Revoke(SessionRevokeReason.Logout, _clock.UtcNow);
        Assert.False((await CreateRefresh().RotateAfterPasswordChangeAsync(user.Id, session.Id)).IsSuccess);
    }

    [Fact]
    public async Task Logout_RevokesTheSession_IsIdempotent_AndRequiresTheCsrfHeaderOnWeb()
    {
        var (user, mobile, _) = Start(["Engineer"]);
        var (webUser, web, _) = Start(["Admin"], ClientApp.Admin, ClientPlatform.Web);
        var logout = new LogoutUseCase(_sessionStore, _clock, new FakeUnitOfWork());

        Assert.Equal(LogoutStatus.LoggedOut, (await logout.ExecuteAsync(user.Id, mobile.Id, csrfHeaderPresent: false)).Status);
        Assert.Equal(SessionRevokeReason.Logout, mobile.RevokeReason);
        Assert.Equal(LogoutStatus.LoggedOut, (await new LogoutUseCase(_sessionStore, _clock, new FakeUnitOfWork()).ExecuteAsync(user.Id, mobile.Id, false)).Status);

        var rejected = await new LogoutUseCase(_sessionStore, _clock, new FakeUnitOfWork()).ExecuteAsync(webUser.Id, web.Id, csrfHeaderPresent: false);
        Assert.Equal(LogoutStatus.CsrfHeaderMissing, rejected.Status);
        Assert.Null(web.RevokedAt);
        var accepted = await new LogoutUseCase(_sessionStore, _clock, new FakeUnitOfWork()).ExecuteAsync(webUser.Id, web.Id, csrfHeaderPresent: true);
        Assert.Equal(ClientPlatform.Web, accepted.Platform);
        Assert.NotNull(web.RevokedAt);
    }

    [Fact]
    public async Task Logout_OfAnotherUsersSession_ChangesNothing()
    {
        var (_, session, _) = Start(["Engineer"]);
        var result = await new LogoutUseCase(_sessionStore, _clock, new FakeUnitOfWork()).ExecuteAsync(Guid.CreateVersion7(), session.Id, true);
        Assert.Equal(LogoutStatus.LoggedOut, result.Status);
        Assert.Null(session.RevokedAt);
    }
}
