using Contractra.Application.Identity;
using Contractra.Application.Identity.Auth;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Auth;

/// <summary>Auth contract v1.5 §5.1 check order, §1 limits, §4 app access, §17 throttling, R33, R42.</summary>
public class LoginUseCaseTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeUserAccessReader _access = new();
    private readonly CountingPasswordHasher _hasher = new();
    private readonly FakeRehashStore _rehash = new();
    private readonly FakeAuthSessionRepository _sessions = new();
    private readonly FakeRefreshTokenFactory _refreshTokens = new();
    private readonly FakeAccessTokenIssuer _accessTokens = new();
    private readonly FakeAuthThrottle _throttle = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private LoginUseCase CreateUseCase() => new(
        _users, _access, _hasher, new DummyPasswordHash(_hasher), _rehash, _sessions, _refreshTokens, _accessTokens, _throttle, _clock, _unitOfWork);

    private User SeedUser(string login = "eng.ahmed", string password = "Passw0rd-1", UserStatus status = UserStatus.Active, bool mustChange = false, DateTimeOffset? tempExpiry = null, string? hash = null)
    {
        var user = new User(new LoginIdentifier(login), UserStatus.Active, hash ?? _hasher.Hash(password), mustChange, mustChange ? tempExpiry ?? _clock.UtcNow.AddHours(72) : null);
        if (status == UserStatus.Disabled)
        {
            user.Disable();
        }

        _users.Seed(user);
        _access.Set(user.Id, ["Engineer"], new() { ["Engineer"] = ["Apps.Employee.Access"] });
        return user;
    }

    private static LoginRequest Request(string? login = "eng.ahmed", string? password = "Passw0rd-1", string? app = "employee", string? platform = null) =>
        new(login, password, app, platform, "203.0.113.1");

    [Fact]
    public async Task Success_StartsASessionForTheApp_AndIssuesTokensWithTheCurrentClaims()
    {
        var user = SeedUser();

        var outcome = await CreateUseCase().ExecuteAsync(Request());

        Assert.True(outcome.IsSuccess);
        var session = Assert.Single(_sessions.Sessions);
        Assert.Equal(user.Id, session.UserId);
        Assert.Equal(ClientApp.Employee, session.ClientApp);
        Assert.Equal(ClientPlatform.Mobile, session.ClientPlatform);
        Assert.Equal(_clock.UtcNow.AddDays(7), session.AbsoluteExpiresAt);
        Assert.True(_unitOfWork.Committed);
        var subject = Assert.Single(_accessTokens.Issued);
        Assert.Equal(session.Id, subject.SessionId);
        Assert.Equal(user.SecurityVersion, subject.SecurityVersion);
        Assert.Equal(["Apps.Employee.Access"], subject.Permissions);
        Assert.Equal(["Engineer"], subject.Roles);
        Assert.NotNull(outcome.Value!.RefreshToken);
        Assert.Contains("credential-success", _throttle.Calls);
    }

    [Fact]
    public async Task ThrottlingRunsFirst_BeforeShapeLookupOrHashing()
    {
        SeedUser();
        _throttle.NextStatus = ThrottleStatus.Throttled;

        var outcome = await CreateUseCase().ExecuteAsync(Request(app: "not-an-app"));

        Assert.Equal(AuthFailureKind.Throttled, outcome.Failure!.Kind);
        Assert.Equal(AuthErrorCodes.TooManyAttempts, outcome.Failure.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), outcome.Failure.RetryAfter);
        Assert.Empty(_hasher.VerifiedHashes);
    }

    [Fact]
    public async Task ThrottleStoreUnavailable_FailsClosed()
    {
        SeedUser();
        _throttle.NextStatus = ThrottleStatus.Unavailable;

        var outcome = await CreateUseCase().ExecuteAsync(Request());

        Assert.Equal(AuthFailureKind.ThrottleUnavailable, outcome.Failure!.Kind);
        Assert.Empty(_hasher.VerifiedHashes);
        Assert.Empty(_sessions.Sessions);
    }

    [Fact]
    public async Task ThrottleKey_IsTheHashOfTheLowerCasedIdentifier()
    {
        SeedUser(login: "Eng.Ahmed");
        await CreateUseCase().ExecuteAsync(Request(login: "ENG.AHMED"));
        Assert.Contains($"admit-credential:{ThrottleKeys.ToHex(ThrottleKeys.IdentifierKey("eng.ahmed"))}:203.0.113.1", _throttle.Calls);
    }

    [Theory]
    [InlineData(null, "p", "employee", null, "loginIdentifier")]
    [InlineData("", "p", "employee", null, "loginIdentifier")]
    [InlineData("u", null, "employee", null, "password")]
    [InlineData("u", "p", null, null, "clientApp")]
    [InlineData("u", "p", "Employee", null, "clientApp")]
    [InlineData("u", "p", "employee", "Web", "X-Client-Platform")]
    public async Task Shape_IsValidationFailed(string? login, string? password, string? app, string? platform, string field)
    {
        var outcome = await CreateUseCase().ExecuteAsync(Request(login, password, app, platform));
        Assert.Equal(AuthErrorCodes.ValidationFailed, outcome.Failure!.Code);
        Assert.Contains(field, outcome.Failure.FieldErrors!.Keys);
        Assert.Empty(_hasher.VerifiedHashes);
    }

    [Fact]
    public async Task OverLimitInput_IsTheGeneric401_WithoutLookupOrHashing()
    {
        SeedUser();
        var longIdentifier = await CreateUseCase().ExecuteAsync(Request(login: new string('a', 257)));
        var longPassword = await CreateUseCase().ExecuteAsync(Request(password: new string('p', 129)));

        Assert.Equal(AuthErrorCodes.InvalidCredentials, longIdentifier.Failure!.Code);
        Assert.Equal(AuthErrorCodes.InvalidCredentials, longPassword.Failure!.Code);
        Assert.Empty(_hasher.VerifiedHashes);
    }

    [Fact]
    public async Task UnknownDisabledAndUnusable_AllVerifyAgainstTheDummyHashOnce()
    {
        var dummy = new DummyPasswordHash(_hasher);
        var useCase = new LoginUseCase(_users, _access, _hasher, dummy, _rehash, _sessions, _refreshTokens, _accessTokens, _throttle, _clock, _unitOfWork);
        SeedUser(login: "disabled", status: UserStatus.Disabled);
        SeedUser(login: "unusable", hash: User.UnusablePasswordHash);

        foreach (var login in new[] { "nobody", "disabled", "unusable" })
        {
            _hasher.VerifiedHashes.Clear();
            var outcome = await useCase.ExecuteAsync(Request(login: login));
            Assert.Equal(AuthErrorCodes.InvalidCredentials, outcome.Failure!.Code);
            Assert.Equal([dummy.Value], _hasher.VerifiedHashes);
        }
    }

    [Fact]
    public async Task WrongPasswordAndExpiredTemporaryPassword_AreTheGeneric401_AfterARealVerification()
    {
        var user = SeedUser(login: "temp", mustChange: true, tempExpiry: _clock.UtcNow);
        var wrong = await CreateUseCase().ExecuteAsync(Request(login: "temp", password: "nope"));
        var expired = await CreateUseCase().ExecuteAsync(Request(login: "temp"));

        Assert.Equal(AuthErrorCodes.InvalidCredentials, wrong.Failure!.Code);
        Assert.Equal(AuthErrorCodes.InvalidCredentials, expired.Failure!.Code);
        Assert.All(_hasher.VerifiedHashes, h => Assert.Equal(user.PasswordHash, h));
        Assert.DoesNotContain("credential-success", _throttle.Calls);
    }

    [Fact]
    public async Task CorrectPasswordWithoutTheAppPermission_IsAppAccessDenied_NamingTheActor()
    {
        var user = SeedUser();

        var outcome = await CreateUseCase().ExecuteAsync(Request(app: "admin"));

        Assert.Equal(AuthErrorCodes.AppAccessDenied, outcome.Failure!.Code);
        Assert.Equal(new DeniedActor(user.Id, AuditSource.Web, "Apps.Admin.Access"), outcome.Failure.DeniedActor);
        Assert.Empty(_sessions.Sessions);
        Assert.Contains("credential-success", _throttle.Calls); // the password was right
    }

    [Fact]
    public async Task WebPlatform_IsStoredOnTheSession()
    {
        SeedUser();
        var outcome = await CreateUseCase().ExecuteAsync(Request(platform: "web"));
        Assert.Equal(ClientPlatform.Web, outcome.Value!.Platform);
        Assert.Equal(ClientPlatform.Web, Assert.Single(_sessions.Sessions).ClientPlatform);
    }

    [Fact]
    public async Task SuccessRehashNeeded_RehashesInTheLoginTransaction_WithoutBumpingTheSecurityVersion()
    {
        var user = SeedUser();
        var version = user.SecurityVersion;
        _hasher.Override = PasswordVerificationResult.SuccessRehashNeeded;

        var outcome = await CreateUseCase().ExecuteAsync(Request());

        Assert.True(outcome.IsSuccess);
        var call = Assert.Single(_rehash.Calls);
        Assert.Equal(user.Id, call.UserId);
        Assert.Equal(user.PasswordHash, call.Old);
        Assert.Equal(version, user.SecurityVersion);
    }

    [Fact]
    public async Task MustChangePassword_IsReported()
    {
        SeedUser(mustChange: true);
        var outcome = await CreateUseCase().ExecuteAsync(Request());
        Assert.True(outcome.Value!.MustChangePassword);
    }
}
