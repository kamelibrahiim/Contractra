using Contractra.Application.Identity.Operations;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Contractra.Application.Tests.Identity.Operations;

/// <summary>Auth contract v1.5 §11 "Creation", §13, §14; owner decisions D2, C3, R19, R26, R41; repository ADR 0006 §5.3, §12.</summary>
public class SeedBootstrapAdminUseCaseTests
{
    private const string Login = "bootstrap.admin";
    private const string Password = "Sup3r-Secret-Bootstrap!";

    private readonly FakeUserRepository _users = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakePermissionRepository _permissions = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeAuditWriter _auditWriter = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ListLogger<SeedBootstrapAdminUseCase> _logger = new();
    private readonly Role _adminRole = new(new RoleCode("Admin"), RoleStatus.Active);
    private readonly Permission _payrollView = new(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active);

    public SeedBootstrapAdminUseCaseTests()
    {
        _roles.Seed(_adminRole);
        _users.RegisterRole(_adminRole);
        _permissions.Seed(_payrollView);
    }

    private SeedBootstrapAdminUseCase CreateUseCase() =>
        new(_users, _roles, _permissions, _hasher, _auditWriter, _clock, _unitOfWork, _logger);

    private static SeedBootstrapAdminRequest Request(string? login = Login, string? password = Password, string correlation = "run-1") =>
        new(login, password, correlation);

    private async Task<User> CreateAsync()
    {
        var outcome = await CreateUseCase().ExecuteAsync(Request());
        Assert.Equal(BootstrapSeedOutcome.Created, outcome);
        return await _users.FindBootstrapAdminAsync() ?? throw new InvalidOperationException("No bootstrap admin was created.");
    }

    [Fact]
    public async Task Seed_WithNoAdmin_CreatesTheBootstrapAdminWithTheAdminRoleAndADirectPayrollViewGrant()
    {
        var user = await CreateAsync();

        Assert.True(user.IsBootstrapAdmin);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.False(user.MustChangePassword);                       // R19
        Assert.Null(user.TemporaryPasswordExpiresAt);
        Assert.Equal(_hasher.Hash(Password), user.PasswordHash);     // hashed by the Application's hasher, never stored raw
        Assert.Equal(Login, user.LoginIdentifier.Value);
        Assert.Equal(_adminRole.Id, Assert.Single(user.Roles).RoleId);
        Assert.Equal(_payrollView.Id, Assert.Single(user.Permissions).PermissionId); // C3: direct grant, not via a role
        Assert.True(_unitOfWork.Committed);
    }

    [Fact]
    public async Task Seed_StagesExactlyUserCreatedRoleAssignedAndPayrollViewGranted_AsSystemSystemJobEventsWithTheRunCorrelation()
    {
        var user = await CreateAsync();

        Assert.Equal(
            new[] { AuditActionType.UserCreated, AuditActionType.RoleAssigned, AuditActionType.PayrollViewGranted },
            _auditWriter.Staged.Select(e => e.ActionType));

        foreach (var auditEvent in _auditWriter.Staged)
        {
            Assert.Equal(AuditActorType.System, auditEvent.ActorType);
            Assert.Null(auditEvent.ActorUserId);                     // no fabricated system user
            Assert.Null(auditEvent.ActorRoleCode);                   // always null for a System actor (contract §14.1)
            Assert.Equal(AuditSource.SystemJob, auditEvent.Source);
            Assert.Equal("User", auditEvent.EntityType);
            Assert.Equal(user.Id, auditEvent.EntityId);
            Assert.Equal("run-1", auditEvent.CorrelationId);
            Assert.Equal(_clock.UtcNow, auditEvent.OccurredAtUtc);   // one clock read for the run
            Assert.Equal(SeedBootstrapAdminUseCase.SeedReason, auditEvent.Reason);
            Assert.Null(auditEvent.PreviousStatus);
            Assert.Null(auditEvent.NewStatus);
        }
    }

    [Fact]
    public async Task Seed_AuditEvents_CarryTheStableBootstrapIdempotencyKeys()
    {
        var user = await CreateAsync();

        Assert.Equal(
            new[]
            {
                $"UserCreated:bootstrap:{user.Id}",
                $"RoleAssigned:bootstrap:{user.Id}",
                $"PayrollViewGranted:bootstrap:{user.Id}",
            },
            _auditWriter.Staged.Select(e => e.IdempotencyKey));
    }

    [Fact]
    public async Task Seed_AuditSnapshots_RecordTheAssignmentAndTheGrant_AndNoSecret()
    {
        var user = await CreateAsync();

        Assert.Null(_auditWriter.Staged[0].Snapshot);

        var role = _auditWriter.Staged[1];
        AuditAssert.SnapshotKeys(role, "user_role_id", "role_id", "role_code", "assigned_at", "ended_at");
        var roleSnapshot = AuditAssert.SnapshotOf(role);
        Assert.Equal(user.Roles.Single().Id.ToString(), roleSnapshot["user_role_id"]);
        Assert.Equal(_adminRole.Id.ToString(), roleSnapshot["role_id"]);
        Assert.Equal("Admin", roleSnapshot["role_code"]);
        AuditAssert.SnapshotTime(roleSnapshot, "assigned_at", _clock.UtcNow);
        Assert.Null(roleSnapshot["ended_at"]);

        var grant = _auditWriter.Staged[2];
        AuditAssert.SnapshotKeys(grant, "user_permission_id", "permission_id", "permission_code", "granted_at", "revoked_at");
        var grantSnapshot = AuditAssert.SnapshotOf(grant);
        Assert.Equal(user.Permissions.Single().Id.ToString(), grantSnapshot["user_permission_id"]);
        Assert.Equal(_payrollView.Id.ToString(), grantSnapshot["permission_id"]);
        Assert.Equal("Payroll.View", grantSnapshot["permission_code"]);
        AuditAssert.SnapshotTime(grantSnapshot, "granted_at", _clock.UtcNow);
        Assert.Null(grantSnapshot["revoked_at"]);

        foreach (var auditEvent in _auditWriter.Staged)
        {
            AuditAssert.ContainsNone(auditEvent, Password, user.PasswordHash, Login);
        }
    }

    [Fact]
    public async Task Seed_WhenAnActiveAdminExists_IsANoOp_EvenWithNoConfiguration()
    {
        await CreateAsync();
        _unitOfWork.Reset();
        _auditWriter.Staged.Clear();
        var hashBefore = (await _users.FindBootstrapAdminAsync())!.PasswordHash;

        var outcome = await CreateUseCase().ExecuteAsync(Request(login: null, password: null, correlation: "run-2"));

        Assert.Equal(BootstrapSeedOutcome.AdminAlreadyExists, outcome);
        Assert.Empty(_auditWriter.Staged);
        Assert.False(_unitOfWork.TransactionOpen);
        Assert.False(_unitOfWork.Committed);
        Assert.Equal(hashBefore, (await _users.FindBootstrapAdminAsync())!.PasswordHash);
    }

    [Fact]
    public async Task Seed_NeverOverwritesAnExistingAdminsPassword_WhenTheConfiguredPasswordChanges()
    {
        await CreateAsync();
        _unitOfWork.Reset();
        _auditWriter.Staged.Clear();
        var hashBefore = (await _users.FindBootstrapAdminAsync())!.PasswordHash;

        var outcome = await CreateUseCase().ExecuteAsync(Request(password: "A-Completely-Different-One1", correlation: "run-2"));

        Assert.Equal(BootstrapSeedOutcome.AdminAlreadyExists, outcome);
        Assert.Equal(hashBefore, (await _users.FindBootstrapAdminAsync())!.PasswordHash);
        Assert.Single(_users.AllUsers());
    }

    [Fact]
    public async Task Seed_WhenAnotherUserHoldsTheAdminRole_DoesNotCreateAnotherAdmin()
    {
        var other = new User(new LoginIdentifier("second.admin"), UserStatus.Active, "hash", mustChangePassword: false);
        other.AssignRole(_adminRole, _clock.UtcNow.AddDays(-1), _clock.UtcNow);
        _users.Seed(other);

        var outcome = await CreateUseCase().ExecuteAsync(Request());

        Assert.Equal(BootstrapSeedOutcome.AdminAlreadyExists, outcome);
        Assert.Null(await _users.FindBootstrapAdminAsync());
        Assert.Empty(_auditWriter.Staged);
    }

    [Fact]
    public async Task Seed_WhenOnlyADisabledAdminExists_CreatesTheBootstrapAdmin()
    {
        var disabled = new User(new LoginIdentifier("old.admin"), UserStatus.Disabled, "hash", mustChangePassword: false);
        _users.Seed(disabled);

        var outcome = await CreateUseCase().ExecuteAsync(Request());

        Assert.Equal(BootstrapSeedOutcome.Created, outcome);
    }

    [Theory]
    [InlineData(null, "Sup3r-Secret-Bootstrap!")]
    [InlineData("", "Sup3r-Secret-Bootstrap!")]
    [InlineData("   ", "Sup3r-Secret-Bootstrap!")]
    [InlineData("bootstrap.admin", null)]
    [InlineData("bootstrap.admin", "")]
    public async Task Seed_WithMissingConfiguration_WarnsAndWritesNothing(string? login, string? password)
    {
        var outcome = await CreateUseCase().ExecuteAsync(Request(login, password));

        Assert.Equal(BootstrapSeedOutcome.NotConfigured, outcome);
        Assert.Empty(_users.AllUsers());
        Assert.Empty(_auditWriter.Staged);
        Assert.False(_unitOfWork.TransactionOpen);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain(Password, entry.Message);
    }

    [Theory]
    [InlineData("short")]                               // 5 characters
    [InlineData("1234567")]                             // 7 characters: one short of the limit
    public async Task Seed_WithATooShortPassword_RefusesAndLogsTheRuleNeverTheValue(string password)
    {
        var outcome = await CreateUseCase().ExecuteAsync(Request(password: password));

        Assert.Equal(BootstrapSeedOutcome.InvalidConfiguration, outcome);
        Assert.Empty(_users.AllUsers());
        Assert.Empty(_auditWriter.Staged);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("Bootstrap:AdminPassword", entry.Message);
        Assert.DoesNotContain(password, entry.Message);
    }

    [Fact]
    public async Task Seed_WithATooLongPassword_Refuses()
    {
        var outcome = await CreateUseCase().ExecuteAsync(Request(password: new string('x', 129)));

        Assert.Equal(BootstrapSeedOutcome.InvalidConfiguration, outcome);
        Assert.Empty(_users.AllUsers());
    }

    [Fact]
    public async Task Seed_AtTheLengthBoundaries_Succeeds()
    {
        Assert.Equal(BootstrapSeedOutcome.Created, await CreateUseCase().ExecuteAsync(Request(password: new string('x', 8))));
    }

    [Theory]
    [InlineData(" bootstrap.admin")]
    [InlineData("bootstrap.admin ")]
    public async Task Seed_WithAPaddedLoginIdentifier_Refuses(string login)
    {
        var outcome = await CreateUseCase().ExecuteAsync(Request(login: login));

        Assert.Equal(BootstrapSeedOutcome.InvalidConfiguration, outcome);
        Assert.Empty(_users.AllUsers());
        Assert.Contains("Bootstrap:AdminLoginIdentifier", Assert.Single(_logger.Entries).Message);
    }

    [Fact]
    public async Task Seed_WhenTheLoginIdentifierBelongsToAnotherAccount_RefusesWithoutTouchingIt()
    {
        var existing = new User(new LoginIdentifier("Bootstrap.Admin"), UserStatus.Active, "hash", mustChangePassword: false);
        _users.Seed(existing);

        var outcome = await CreateUseCase().ExecuteAsync(Request());

        Assert.Equal(BootstrapSeedOutcome.InvalidConfiguration, outcome);
        Assert.Equal("hash", existing.PasswordHash);
        Assert.Empty(_auditWriter.Staged);
    }

    [Fact]
    public async Task Seed_WhenTheAdminRoleIsMissing_FailsSoStartupStops_AndWritesNothing()
    {
        var useCase = new SeedBootstrapAdminUseCase(
            _users, new FakeRoleRepository(), _permissions, _hasher, _auditWriter, _clock, _unitOfWork, _logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(Request()));

        Assert.False(_unitOfWork.TransactionOpen);
        Assert.Empty(_auditWriter.Staged);
    }

    [Fact]
    public async Task Seed_NeverLogsThePassword()
    {
        await CreateAsync();
        await CreateUseCase().ExecuteAsync(Request(password: "short"));

        Assert.All(_logger.Entries, entry => Assert.DoesNotContain(Password, entry.Message));
        Assert.All(_logger.Entries, entry => Assert.DoesNotContain("short", entry.Message));
    }
}
