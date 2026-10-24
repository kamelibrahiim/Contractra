using Contractra.Application.Common;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Auth;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Domain.Parties;

namespace Contractra.Application.Tests.Identity;

/// <summary>Auth contract v1.5 §9.1, owner decisions R2, R9, R30, R31, R43; engineering decisions E4, E8.</summary>
public class CreateUserUseCaseTests
{
    private static readonly Guid ActorId = Guid.CreateVersion7();
    private static readonly DateOnly BusinessToday = new(2026, 10, 16);

    private readonly FakeUserRepository _users = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakePartyRepository _parties = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeTemporaryPasswordGenerator _temporaryPasswords = new();
    private readonly FakeAuditWriter _auditWriter = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeUserAccessReader _accessReader = new();
    private readonly Role _engineerRole = new(new RoleCode("Engineer"), RoleStatus.Active);
    private readonly Role _officeStaffRole = new(new RoleCode("OfficeStaff"), RoleStatus.Active);

    public CreateUserUseCaseTests()
    {
        _roles.Seed(_engineerRole);
        _roles.Seed(_officeStaffRole);
    }

    private CreateUserUseCase CreateUseCase() => new(
        _users, _roles, _parties, _hasher, _temporaryPasswords, _auditWriter, _clock,
        new FixedBusinessDateProvider(BusinessToday), _unitOfWork, _accessReader);

    private Party SeedValidEngineerParty()
    {
        var party = new Party("Ahmed Engineer", PartyStatus.Active);
        party.AssignRole(new PartyRoleCode("Engineer"), BusinessToday.AddDays(-30));
        _parties.Seed(party);
        return party;
    }

    [Fact]
    public async Task CreateUser_WithoutEngineerRole_CreatesAnActiveUserWithATemporaryPassword()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", new[] { "OfficeStaff" }, PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal("office.sara", result.Value!.LoginIdentifier);
        Assert.NotEmpty(result.Value.TemporaryPassword);
        Assert.True(_unitOfWork.Committed);

        var user = await _users.FindByIdAsync(result.Value.UserId);
        Assert.NotNull(user);
        Assert.Equal(UserStatus.Active, user!.Status);
        Assert.True(user.MustChangePassword);
        Assert.Equal(_hasher.Hash(result.Value.TemporaryPassword), user.PasswordHash);
        Assert.Single(user.Roles);
    }

    [Fact]
    public async Task CreateUser_StagesUserCreatedAndRoleAssignedAuditEvents()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", new[] { "OfficeStaff" }, PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _auditWriter.Staged.Count);
        Assert.Contains(_auditWriter.Staged, e => e.ActionType == AuditActionType.UserCreated);
        Assert.Contains(_auditWriter.Staged, e => e.ActionType == AuditActionType.RoleAssigned);
    }

    [Fact]
    public async Task CreateUser_WithZeroRoles_Fails()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", Array.Empty<string>(), PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.role_required", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_WithANulCharacterInTheLoginIdentifierOrARoleCode_FailsWithoutTouchingTheStore()
    {
        var useCase = CreateUseCase();

        var login = await useCase.ExecuteAsync(new CreateUserRequest(
            "office\0sara", new[] { "OfficeStaff" }, PartyId: null, ActorId, "corr-1", AuditSource.Web));
        var role = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", new[] { "Office\0Staff" }, PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.Equal("users.login_identifier_invalid", login.Error!.Code);
        Assert.Equal("users.role_unknown", role.Error!.Code);
        Assert.False(_unitOfWork.Committed);
    }

    [Fact]
    public async Task CreateUser_WithDuplicateRoleCodes_Fails()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", new[] { "OfficeStaff", "officestaff" }, PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("validation.failed", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_WithAnUnknownRole_Fails()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", new[] { "NoSuchRole" }, PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.role_unknown", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_WithADeprecatedRole_Fails()
    {
        var deprecated = new Role(new RoleCode("Retired"), RoleStatus.Active);
        deprecated.Deprecate();
        _roles.Seed(deprecated);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", new[] { "Retired" }, PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.role_unknown", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_WithALoginIdentifierAlreadyTaken_Fails()
    {
        _users.Seed(new User(new LoginIdentifier("office.sara"), UserStatus.Active, "hash", mustChangePassword: false));
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", new[] { "OfficeStaff" }, PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.login_identifier_taken", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithoutPartyId_Fails()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "eng.ahmed", new[] { "Engineer" }, PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.party_required", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_ForANonEngineerRole_WithAPartyId_Fails()
    {
        var party = SeedValidEngineerParty();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", new[] { "OfficeStaff" }, party.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("validation.failed", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithAValidUnlinkedParty_LinksItInTheSameTransaction()
    {
        var party = SeedValidEngineerParty();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "eng.ahmed", new[] { "Engineer" }, party.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        var user = await _users.FindByIdAsync(result.Value!.UserId);
        Assert.Equal(party.Id, user!.PartyLink!.PartyId);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithAnUnknownParty_Fails()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "eng.ahmed", new[] { "Engineer" }, Guid.CreateVersion7(), ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.party_invalid", result.Error!.Code);
        Assert.Equal("not_found", result.Error.Message);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithAnInactiveParty_Fails()
    {
        var party = new Party("Ahmed", PartyStatus.NeedsClassification);
        party.AssignRole(new PartyRoleCode("Engineer"), BusinessToday.AddDays(-30));
        _parties.Seed(party);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "eng.ahmed", new[] { "Engineer" }, party.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.party_invalid", result.Error!.Code);
        Assert.Equal("not_active", result.Error.Message);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithAPartyLackingAnActiveEngineerRole_Fails()
    {
        var party = new Party("Ahmed", PartyStatus.Active);
        // No Engineer party role at all.
        _parties.Seed(party);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "eng.ahmed", new[] { "Engineer" }, party.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.party_invalid", result.Error!.Code);
        Assert.Equal("no_active_engineer_role", result.Error.Message);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithAnExpiredEngineerPartyRole_Fails()
    {
        var party = new Party("Ahmed", PartyStatus.Active);
        party.AssignRole(new PartyRoleCode("Engineer"), BusinessToday.AddDays(-60), BusinessToday.AddDays(-1));
        _parties.Seed(party);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "eng.ahmed", new[] { "Engineer" }, party.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.party_invalid", result.Error!.Code);
        Assert.Equal("no_active_engineer_role", result.Error.Message);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithAPartyAlreadyLinkedToAUser_Fails()
    {
        var party = SeedValidEngineerParty();
        _parties.MarkLinked(party.Id);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "eng.ahmed", new[] { "Engineer" }, party.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.party_already_linked", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_WithALeadingOrTrailingWhitespaceLoginIdentifier_Fails()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            " office.sara ", new[] { "OfficeStaff" }, PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.login_identifier_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_ARejectedRequest_NeverOpensATransaction()
    {
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", Array.Empty<string>(), PartyId: null, ActorId, "corr-1", AuditSource.Web));

        Assert.False(_unitOfWork.TransactionOpen);
        Assert.False(_unitOfWork.Committed);
    }

    private sealed class FixedBusinessDateProvider : IBusinessDateProvider
    {
        private readonly DateOnly _today;

        public FixedBusinessDateProvider(DateOnly today) => _today = today;

        public DateOnly Today() => _today;
    }

    [Fact]
    public async Task CreateUser_AuditEvents_CarryActorSourceCorrelationAndTheRoleSnapshot()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara", new[] { "OfficeStaff" }, PartyId: null, ActorId, "corr-create", AuditSource.Web));

        var userId = result.Value!.UserId;
        var user = await _users.FindByIdAsync(userId);
        var assignment = Assert.Single(user!.Roles);

        // OC-6 resolved: ActorId has no active role in this fake, so the
        // confirmed rule itself (not an unresolved OC-6) is what yields null here.
        var created = Assert.Single(_auditWriter.Staged, e => e.ActionType == AuditActionType.UserCreated);
        AuditAssert.Event(
            created, AuditActionType.UserCreated, ActorId, userId, "corr-create", AuditSource.Web, _clock.UtcNow,
            actorRoleCodeStillPendingOc6: false, expectedActorRoleCode: null);
        Assert.Null(created.Snapshot);

        var assigned = Assert.Single(_auditWriter.Staged, e => e.ActionType == AuditActionType.RoleAssigned);
        AuditAssert.Event(
            assigned, AuditActionType.RoleAssigned, ActorId, userId, "corr-create", AuditSource.Web, _clock.UtcNow,
            actorRoleCodeStillPendingOc6: false, expectedActorRoleCode: null);
        AuditAssert.SnapshotKeys(assigned, "user_role_id", "role_id", "role_code", "assigned_at", "ended_at");
        var snapshot = AuditAssert.SnapshotOf(assigned);
        Assert.Equal(assignment.Id.ToString(), snapshot["user_role_id"]);
        Assert.Equal(_officeStaffRole.Id.ToString(), snapshot["role_id"]);
        Assert.Equal("OfficeStaff", snapshot["role_code"]);
        AuditAssert.SnapshotTime(snapshot, "assigned_at", _clock.UtcNow);
        Assert.Null(snapshot["ended_at"]);

        foreach (var auditEvent in _auditWriter.Staged)
        {
            AuditAssert.ContainsNone(auditEvent, result.Value.TemporaryPassword, user.PasswordHash, "office.sara");
        }
    }

    // Contract §14.1, owner decision OC-6: creating a user requires Users.Manage,
    // so the rule resolves to the active role through which the actor held it
    // (several → first by ordinal code order) - "Accountant" sorts before "Admin".
    [Fact]
    public async Task CreateUser_AuditEvents_ActorRoleCode_IsTheRoleGrantingUsersManage()
    {
        _accessReader.Set(
            ActorId, ["Admin", "Accountant"],
            new() { ["Admin"] = ["Users.Manage"], ["Accountant"] = ["Users.Manage"] });
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara2", new[] { "OfficeStaff" }, PartyId: null, ActorId, "corr-create", AuditSource.Web));

        var userId = result.Value!.UserId;
        var created = Assert.Single(_auditWriter.Staged, e => e.ActionType == AuditActionType.UserCreated);
        Assert.Equal("Accountant", created.ActorRoleCode);
        var assigned = Assert.Single(_auditWriter.Staged, e => e.ActionType == AuditActionType.RoleAssigned);
        Assert.Equal("Accountant", assigned.ActorRoleCode);
    }

    // No role grants Users.Manage directly (e.g. it came from a direct grant to
    // the actor): the rule falls back to the actor's first active role.
    [Fact]
    public async Task CreateUser_AuditEvents_ActorRoleCode_FallsBackToTheFirstActiveRole_WhenNoRoleGrantsUsersManage()
    {
        _accessReader.Set(ActorId, ["OfficeStaff", "Engineer"], direct: ["Users.Manage"]);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "office.sara3", new[] { "OfficeStaff" }, PartyId: null, ActorId, "corr-create", AuditSource.Web));

        var created = Assert.Single(_auditWriter.Staged, e => e.ActionType == AuditActionType.UserCreated);
        Assert.Equal("Engineer", created.ActorRoleCode);
    }

    [Fact]
    public async Task CreateUser_WithSeveralRoles_StagesOneRoleAssignedEventPerRoleEachWithItsOwnSnapshot()
    {
        var useCase = CreateUseCase();
        var party = SeedValidEngineerParty();

        var result = await useCase.ExecuteAsync(new CreateUserRequest(
            "eng.ahmed", new[] { "Engineer", "OfficeStaff" }, party.Id, ActorId, "corr-multi", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, _auditWriter.Staged.Count);
        var snapshots = _auditWriter.Staged
            .Where(e => e.ActionType == AuditActionType.RoleAssigned)
            .Select(AuditAssert.SnapshotOf)
            .ToList();
        Assert.Equal(new[] { "Engineer", "OfficeStaff" }, snapshots.Select(s => s["role_code"]).OrderBy(c => c));
        Assert.Equal(2, snapshots.Select(s => s["user_role_id"]).Distinct().Count());

        // OC-6: the actor's role is resolved once and reused for every event of
        // this request, not recomputed per RoleAssigned iteration.
        Assert.Single(_auditWriter.Staged.Select(e => e.ActorRoleCode).Distinct());
    }
}
