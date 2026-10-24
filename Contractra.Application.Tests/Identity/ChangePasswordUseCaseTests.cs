using Contractra.Application.Identity;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Auth;
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
    private readonly FakeUserAccessReader _accessReader = new();

    private ChangePasswordUseCase CreateUseCase() =>
        new(_users, _sessions, _hasher, _auditWriter, _clock, _unitOfWork, _accessReader);

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
        var useCase = new ChangePasswordUseCase(_users, _sessions, _hasher, _auditWriter, _clock, _unitOfWork, _accessReader);

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
    public async Task ChangePassword_WithAnExpiredTemporaryPassword_FailsAndChangesNothing()
    {
        // Contract §5.5/§10: during a forced change the current password is the
        // temporary one, which must not have expired. The same expiry rule and
        // clock as login (§5.1), checked after the hash matches so the response
        // and the timing are the same as a wrong hash.
        var temporaryHash = _hasher.Hash("TempPassw0rd1");
        var user = new User(
            new LoginIdentifier("eng.ahmed"), UserStatus.Active, temporaryHash, mustChangePassword: true, _clock.UtcNow.AddHours(72));
        _users.Seed(user);
        _clock.UtcNow = user.TemporaryPasswordExpiresAt!.Value; // exactly at expiry: the same "<=" boundary as login
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "TempPassw0rd1", "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.current_password_incorrect", result.Error!.Code);
        Assert.Equal(temporaryHash, user.PasswordHash);
        Assert.True(user.MustChangePassword);
        Assert.Equal(1, user.SecurityVersion);
        Assert.False(_unitOfWork.TransactionOpen);
        Assert.False(_unitOfWork.Committed);
        Assert.Empty(_auditWriter.Staged);
        Assert.Empty(_sessions.RevokeAllCalls);
    }

    [Fact]
    public async Task ChangePassword_WithATemporaryPasswordNotYetExpired_Succeeds()
    {
        var temporaryHash = _hasher.Hash("TempPassw0rd1");
        var user = new User(
            new LoginIdentifier("eng.ahmed"), UserStatus.Active, temporaryHash, mustChangePassword: true, _clock.UtcNow.AddHours(72));
        _users.Seed(user);
        _clock.UtcNow = user.TemporaryPasswordExpiresAt!.Value.AddSeconds(-1); // one second before the boundary
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "TempPassw0rd1", "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal(_hasher.Hash("NewPassw0rd"), user.PasswordHash);
        Assert.False(user.MustChangePassword);
    }

    // Contract §5.5 lists currentPassword correctness before the newPassword
    // length rules: a wrong current password must win over a too-short or
    // too-long new password, not the other way around.
    [Theory]
    [InlineData("Sh0rt1")]
    [InlineData("")]
    public async Task ChangePassword_WithWrongCurrentPassword_AndATooShortNewPassword_StillReturnsCurrentPasswordIncorrect(string tooShort)
    {
        var user = SeedActiveUser();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "WrongPassword", tooShort, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.current_password_incorrect", result.Error!.Code);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_AndATooLongNewPassword_StillReturnsCurrentPasswordIncorrect()
    {
        var user = SeedActiveUser();
        var useCase = CreateUseCase();
        var tooLong = new string('a', 129);

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "WrongPassword", tooLong, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.current_password_incorrect", result.Error!.Code);
    }

    // Same precedence, for the expired-temporary-password path: the generic
    // current-password-incorrect code still wins over a bad new-password length.
    [Fact]
    public async Task ChangePassword_WithAnExpiredTemporaryPassword_AndATooShortNewPassword_StillReturnsCurrentPasswordIncorrect()
    {
        var temporaryHash = _hasher.Hash("TempPassw0rd1");
        var user = new User(
            new LoginIdentifier("eng.ahmed"), UserStatus.Active, temporaryHash, mustChangePassword: true, _clock.UtcNow.AddHours(72));
        _users.Seed(user);
        _clock.UtcNow = user.TemporaryPasswordExpiresAt!.Value;
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "TempPassw0rd1", "x", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.current_password_incorrect", result.Error!.Code);
    }

    [Fact]
    public async Task ChangePassword_WithAnExpiredTemporaryPassword_AndATooLongNewPassword_StillReturnsCurrentPasswordIncorrect()
    {
        var temporaryHash = _hasher.Hash("TempPassw0rd1");
        var user = new User(
            new LoginIdentifier("eng.ahmed"), UserStatus.Active, temporaryHash, mustChangePassword: true, _clock.UtcNow.AddHours(72));
        _users.Seed(user);
        _clock.UtcNow = user.TemporaryPasswordExpiresAt!.Value;
        var useCase = CreateUseCase();
        var tooLong = new string('a', 129);

        var result = await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "TempPassw0rd1", tooLong, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.current_password_incorrect", result.Error!.Code);
    }

    [Fact]
    public async Task ChangePassword_AuditEvent_NamesTheUserAsActorAndTargetWithTheCallersSourceAndCorrelation()
    {
        var user = SeedActiveUser();
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "OldPassw0rd", "NewPassw0rd", "corr-change", AuditSource.EmployeeApp));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        // OC-6 resolved: this seeded user has no active role, so the confirmed
        // rule itself (not an unresolved OC-6) is what yields null here.
        AuditAssert.Event(
            auditEvent, AuditActionType.PasswordChanged, user.Id, user.Id, "corr-change", AuditSource.EmployeeApp, _clock.UtcNow,
            actorRoleCodeStillPendingOc6: false, expectedActorRoleCode: null);
        Assert.Null(auditEvent.Snapshot);
        AuditAssert.ContainsNone(auditEvent, "OldPassw0rd", "NewPassw0rd", user.PasswordHash);
    }

    // Contract §14.1, owner decision OC-6: changing one's own password needs no
    // permission, so the rule resolves to the actor's first active role by
    // ordinal code order - "Accountant" sorts before "Engineer" and "OfficeStaff".
    [Fact]
    public async Task ChangePassword_AuditEvent_ActorRoleCode_IsTheFirstActiveRoleByOrdinalOrder_WhenTheActorHasSeveral()
    {
        var user = SeedActiveUser();
        _accessReader.Set(user.Id, ["OfficeStaff", "Accountant", "Engineer"]);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ChangePasswordRequest(
            user.Id, SessionId, "OldPassw0rd", "NewPassw0rd", "corr-change", AuditSource.EmployeeApp));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal("Accountant", auditEvent.ActorRoleCode);
    }
}
