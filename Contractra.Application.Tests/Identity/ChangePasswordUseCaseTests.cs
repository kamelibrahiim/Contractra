using Contractra.Application.Identity;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity;

/// <summary>Auth contract v1.5 §5.5.</summary>
public class ChangePasswordUseCaseTests
{
    private static readonly Guid SessionId = Guid.CreateVersion7();

    private readonly FakeUserRepository _users = new();
    private readonly FakeSessionStore _sessions = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeAuditWriter _auditWriter = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private ChangePasswordUseCase CreateUseCase() =>
        new(_users, _sessions, _hasher, _auditWriter, _clock, _unitOfWork);

    private User SeedActiveUser(string currentPassword = "OldPassw0rd")
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, _hasher.Hash(currentPassword), mustChangePassword: false);
        _users.Seed(user);
        return user;
    }

    [Fact]
    public async Task ChangePassword_WithCorrectCurrentPassword_UpdatesHashAndClearsForcedChange()
    {
        var user = SeedActiveUser();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "OldPassw0rd", "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal(_hasher.Hash("NewPassw0rd"), user.PasswordHash);
        Assert.False(user.MustChangePassword);
    }

    [Fact]
    public async Task ChangePassword_BumpsSecurityVersionAndCommitsInOneTransaction()
    {
        var user = SeedActiveUser();
        var versionBefore = user.SecurityVersion;
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "OldPassw0rd", "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.Equal(versionBefore + 1, user.SecurityVersion);
        Assert.True(_unitOfWork.Committed);
    }

    [Fact]
    public async Task ChangePassword_RevokesEveryOtherSession_ButKeepsTheCurrentOne()
    {
        var user = SeedActiveUser();
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "OldPassw0rd", "NewPassw0rd", "corr-1", AuditSource.Web));

        var call = Assert.Single(_sessions.RevokeAllCalls);
        Assert.Equal(user.Id, call.UserId);
        Assert.Equal(SessionRevokeReason.PasswordChanged, call.Reason);
        Assert.Equal(SessionId, call.ExceptSessionId);
    }

    [Fact]
    public async Task ChangePassword_LeavesTheCurrentSessionActiveAndStillRotatable_SoSlice6CanRotateItAfterCommit()
    {
        // This is the exact transaction boundary Slice 6 depends on: this use
        // case commits the password change and the other-session revocation
        // by itself, and deliberately leaves the current session untouched so
        // a later, separate rotation step is still legal. It does not perform
        // that rotation itself (Slice 6's job, at the HTTP boundary).
        var user = SeedActiveUser();
        var firstTokenHash = Enumerable.Repeat((byte)7, 32).ToArray();
        var session = AuthSession.Start(
            user.Id, ClientApp.Admin, ClientPlatform.Web, firstTokenHash, _clock.UtcNow.AddMinutes(-5), _clock.UtcNow.AddDays(7));
        _sessions.Seed(session);
        var useCase = new ChangePasswordUseCase(_users, _sessions, _hasher, _auditWriter, _clock, _unitOfWork);

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, session.Id, "OldPassw0rd", "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        var reloaded = await _sessions.FindByIdAsync(session.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.IsActiveAt(_clock.UtcNow));

        // Rotation is still legal on it after this use case's transaction has committed.
        var newTokenHash = Enumerable.Repeat((byte)9, 32).ToArray();
        var rotated = reloaded.Rotate(firstTokenHash, newTokenHash, _clock.UtcNow.AddMinutes(1));
        Assert.NotNull(rotated);
    }

    [Fact]
    public async Task ChangePassword_StagesAPasswordChangedAuditEvent()
    {
        var user = SeedActiveUser();
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "OldPassw0rd", "NewPassw0rd", "corr-1", AuditSource.Web));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal(AuditActionType.PasswordChanged, auditEvent.ActionType);
        Assert.Equal(user.Id, auditEvent.ActorUserId);
        Assert.Equal("corr-1", auditEvent.CorrelationId);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_FailsAndChangesNothing()
    {
        var user = SeedActiveUser();
        var hashBefore = user.PasswordHash;
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "WrongPassword", "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.current_password_incorrect", result.Error!.Code);
        Assert.Equal(hashBefore, user.PasswordHash);
        Assert.False(_unitOfWork.TransactionOpen);
        Assert.False(_unitOfWork.Committed);
        Assert.Empty(_auditWriter.Staged);
    }

    [Fact]
    public async Task ChangePassword_WhenNewPasswordEqualsCurrent_Fails()
    {
        var user = SeedActiveUser();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "OldPassw0rd", "OldPassw0rd", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.password_same_as_current", result.Error!.Code);
    }

    [Theory]
    [InlineData("Sh0rt1")]
    public async Task ChangePassword_WithATooShortNewPassword_Fails(string tooShort)
    {
        var user = SeedActiveUser();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "OldPassw0rd", tooShort, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.password_too_short", result.Error!.Code);
    }

    [Fact]
    public async Task ChangePassword_WithATooLongNewPassword_Fails()
    {
        var user = SeedActiveUser();
        var useCase = CreateUseCase();
        var tooLong = new string('a', 129);

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "OldPassw0rd", tooLong, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.password_too_long", result.Error!.Code);
    }

    [Fact]
    public async Task ChangePassword_ByTheBootstrapAdmin_OnItsOwnAccount_Succeeds()
    {
        var adminRole = new Role(new RoleCode("Admin"), RoleStatus.Active);
        var payrollView = new Permission(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active);
        var admin = User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), _hasher.Hash("BootstrapPassw0rd"), adminRole, payrollView, _clock.UtcNow, _clock.UtcNow);
        _users.Seed(admin);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            admin.Id, SessionId, "BootstrapPassw0rd", "NewBootstrapPassw0rd", "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal(_hasher.Hash("NewBootstrapPassw0rd"), admin.PasswordHash);
    }

    [Fact]
    public async Task ChangePassword_AuditEvent_NamesTheUserAsActorAndTargetWithTheCallersSourceAndCorrelation()
    {
        var user = SeedActiveUser();
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "OldPassw0rd", "NewPassw0rd", "corr-change", AuditSource.EmployeeApp));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        AuditAssert.Event(auditEvent, AuditActionType.PasswordChanged, user.Id, user.Id, "corr-change", AuditSource.EmployeeApp, _clock.UtcNow);
        Assert.Null(auditEvent.Snapshot);
        AuditAssert.ContainsNone(auditEvent, "OldPassw0rd", "NewPassw0rd", user.PasswordHash);
    }
}
