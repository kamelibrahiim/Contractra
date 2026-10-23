using Contractra.Application.Common;
using Contractra.Application.Identity.Credentials;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Identity.Usernames;
using Contractra.Application.Tests.Identity.Auth;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Domain.Parties;

namespace Contractra.Application.Tests.Identity;

/// <summary>
/// Auth contract v1.6 §23.2 (owner decisions R9, R30, R31, R43, R49-R51; engineering decisions E8, E13; ADR 0007):
/// one generated password per operation, its hash in the user, a sealed copy in the credential issuance, no
/// password in the result, and nothing created when delivery is unavailable.
/// </summary>
public class CreateUserUseCaseTests
{
    private const string Number = "+201000000001";
    private const string GeneratedPassword = "Temp1Aa1"; // FakeTemporaryPasswordGenerator's first value

    private static readonly Guid ActorId = Guid.CreateVersion7();
    private static readonly DateOnly BusinessToday = new(2026, 10, 16);

    private readonly FakeUserRepository _users = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakePartyRepository _parties = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeTemporaryPasswordGenerator _passwords = new();
    private readonly FakeCredentialSealer _sealer = new();
    private readonly StubWhatsAppSender _sender = new();
    private readonly FakeCredentialIssuanceStore _issuances = new();
    private readonly CredentialDeliveryOptions _delivery = new() { Enabled = true };
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
        _users, _roles, _parties, _hasher, _passwords, _sealer, _sender, _issuances, _delivery, new UsernamePolicy(),
        _auditWriter, _clock, new FixedBusinessDateProvider(BusinessToday), _unitOfWork, _accessReader);

    private static CreateUserRequest Request(
        string login = "office.sara",
        string[]? roles = null,
        Guid? partyId = null,
        string? number = Number,
        bool consent = true,
        string? basis = "verbal",
        string correlation = "corr-1") =>
        new(login, roles ?? ["OfficeStaff"], partyId, number, consent, basis, ActorId, correlation, AuditSource.Web);

    private Party SeedValidEngineerParty()
    {
        var party = new Party("Ahmed Engineer", PartyStatus.Active);
        party.AssignRole(new PartyRoleCode("Engineer"), BusinessToday.AddDays(-30));
        _parties.Seed(party);
        return party;
    }

    // ---- the credential issuance (R49-R51, ADR 0007) ------------------------------------------------------

    [Fact]
    public async Task CreateUser_CreatesAnActiveUserWithTheGeneratedPasswordHash_AndNeverForcesAChange()
    {
        var result = await CreateUseCase().ExecuteAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal("office.sara", result.Value!.LoginIdentifier);
        Assert.True(_unitOfWork.Committed);

        var user = await _users.FindByIdAsync(result.Value.UserId);
        Assert.Equal(UserStatus.Active, user!.Status);
        Assert.False(user.MustChangePassword);
        Assert.Null(user.TemporaryPasswordExpiresAt);
        Assert.Equal(_hasher.Hash(GeneratedPassword), user.PasswordHash);
        Assert.Single(user.Roles);
    }

    [Fact]
    public async Task CreateUser_ResultCarriesTheDeliveryStatusAndNoPasswordAnywhere()
    {
        var result = await CreateUseCase().ExecuteAsync(Request());

        var value = result.Value!;
        Assert.Equal(CredentialDeliveryStatus.Queued, value.Delivery.Status);
        Assert.Equal(CredentialIssuanceKind.Initial, value.Delivery.Kind);
        Assert.Equal(0, value.Delivery.Attempts);
        Assert.Equal("+20••••••••01", value.Delivery.DestinationMasked);
        Assert.Null(value.Delivery.FailureCategory);

        // No member of the result can hold the password, and no text form of it shows the password or the number.
        Assert.DoesNotContain(GeneratedPassword, value.ToString());
        Assert.DoesNotContain("1000000001", value.ToString());
        Assert.DoesNotContain(
            typeof(CreateUserResult).GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateUser_QueuesOneInitialIssuanceWithASealedCopyBoundToItsRow()
    {
        var result = await CreateUseCase().ExecuteAsync(Request());

        var issuance = Assert.Single(_issuances.All);
        Assert.Equal(result.Value!.UserId, issuance.UserId);
        Assert.Equal(CredentialIssuanceKind.Initial, issuance.Kind);
        Assert.Equal(CredentialDeliveryStatus.Queued, issuance.Status);
        Assert.Equal(ActorId, issuance.IssuedByUserId);
        Assert.Equal(Number, issuance.Destination);
        Assert.Equal("verbal", issuance.ConsentBasis);
        Assert.Equal(_clock.UtcNow.Add(_delivery.SealedCopyRetention), issuance.PurgeBy);
        Assert.True(issuance.HasSealedCopy);

        // The sealed copy opens, for this row only, to exactly the password whose hash the user holds.
        var opened = _sealer.Open(
            new SealedSecret(issuance.SealedBytes!, issuance.SealNonce!, issuance.SealKeyId!),
            new SealContext(issuance.Id, issuance.UserId));
        Assert.Equal(GeneratedPassword, opened.Reveal());
        Assert.Throws<SealOpenException>(() => _sealer.Open(
            new SealedSecret(issuance.SealedBytes!, issuance.SealNonce!, issuance.SealKeyId!),
            new SealContext(Guid.CreateVersion7(), issuance.UserId)));
    }

    [Theory]
    [InlineData("+201000000001", "+201000000001")]
    [InlineData("0020 100-000 0001", "+201000000001")]
    [InlineData("+20 (100) 000.0001", "+201000000001")]
    public async Task CreateUser_NormalisesTheNumberToE164(string typed, string expected)
    {
        await CreateUseCase().ExecuteAsync(Request(number: typed));

        Assert.Equal(expected, Assert.Single(_issuances.All).Destination);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01000000001")]
    [InlineData("+0123456789")]
    [InlineData("+2010")]
    [InlineData("+2010000000012345")]
    [InlineData("+20100abc0001")]
    public async Task CreateUser_WithAnInvalidNumber_FailsAndCreatesNothing(string? typed)
    {
        var result = await CreateUseCase().ExecuteAsync(Request(number: typed));

        Assert.Equal("users.whatsapp_number_invalid", result.Error!.Code);
        Assert.Empty(_users.AllUsers());
        Assert.Empty(_issuances.All);
        Assert.False(_unitOfWork.Committed);
    }

    [Theory]
    [InlineData(false, "verbal")]
    [InlineData(true, null)]
    [InlineData(true, "other")]
    [InlineData(false, null)]
    public async Task CreateUser_WithoutAffirmedConsentAndAValidBasis_FailsAndCreatesNothing(bool affirmed, string? basis)
    {
        var result = await CreateUseCase().ExecuteAsync(Request(consent: affirmed, basis: basis));

        Assert.Equal("users.whatsapp_consent_required", result.Error!.Code);
        Assert.Empty(_users.AllUsers());
        Assert.Empty(_issuances.All);
    }

    [Fact]
    public async Task CreateUser_WhenDeliveryIsDisabled_Answers503AndCreatesNothing()
    {
        _delivery.Enabled = false;

        await AssertNothingCreatedWith503Async();
    }

    [Fact]
    public async Task CreateUser_WhenNoSenderIsConfigured_Answers503AndCreatesNothing()
    {
        _sender.Configured = false;

        await AssertNothingCreatedWith503Async();
    }

    [Fact]
    public async Task CreateUser_WhenTheSealingKeyIsAbsent_Answers503AndCreatesNothing()
    {
        _sealer.Available = false;

        await AssertNothingCreatedWith503Async();
    }

    private async Task AssertNothingCreatedWith503Async()
    {
        var result = await CreateUseCase().ExecuteAsync(Request());

        Assert.Equal("users.credential_delivery_unavailable", result.Error!.Code);
        Assert.Empty(_users.AllUsers());
        Assert.Empty(_issuances.All);
        Assert.Empty(_auditWriter.Staged);
        Assert.False(_unitOfWork.TransactionOpen);
        Assert.False(_unitOfWork.Committed);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task CreateUser_NeverSendsAnything_ThatIsTheWorkersJobAfterCommit()
    {
        await CreateUseCase().ExecuteAsync(Request());

        Assert.Empty(_sender.Sent);
    }

    // ---- username policy (R49, username-policy-v1) ---------------------------------------------------------

    [Theory]
    [InlineData("ab", "too_short")]
    [InlineData("office sara", "whitespace")]
    [InlineData(" office.sara ", "whitespace")]
    [InlineData("office@sara", "bad_characters")]
    [InlineData("1office", "bad_shape")]
    [InlineData("office.sara.1234567", "phone_like")]
    [InlineData("admin", "reserved")]
    [InlineData("Admin2", "reserved")]
    [InlineData("OfficeStaff", "reserved")] // a role code
    [InlineData("office\0sara", "bad_characters")]
    public async Task CreateUser_WithAnIdentifierThePolicyRefuses_FailsWithTheReason(string login, string reason)
    {
        var result = await CreateUseCase().ExecuteAsync(Request(login: login));

        Assert.Equal("users.login_identifier_invalid", result.Error!.Code);
        Assert.Equal(reason, result.Error.Message);
        Assert.Empty(_users.AllUsers());
    }

    [Fact]
    public async Task CreateUser_StoresTheCanonicalLowercaseIdentifier()
    {
        var result = await CreateUseCase().ExecuteAsync(Request(login: "Office.Sara"));

        Assert.True(result.IsSuccess);
        Assert.Equal("office.sara", result.Value!.LoginIdentifier);
    }

    [Fact]
    public async Task CreateUser_WithTheBootstrapAdminsIdentifier_IsReserved()
    {
        _users.Seed(User.CreateBootstrapAdmin(
            new LoginIdentifier("founder.boss"), "hash", new Role(new RoleCode("Admin"), RoleStatus.Active),
            new Permission(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active), _clock.UtcNow, _clock.UtcNow));

        var result = await CreateUseCase().ExecuteAsync(Request(login: "Founder.Boss"));

        Assert.Equal("users.login_identifier_invalid", result.Error!.Code);
        Assert.Equal("reserved", result.Error.Message);
    }

    // ---- unchanged v1.5 rules -----------------------------------------------------------------------------

    [Fact]
    public async Task CreateUser_StagesUserCreatedAndRoleAssignedAuditEvents()
    {
        var result = await CreateUseCase().ExecuteAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _auditWriter.Staged.Count);
        Assert.Contains(_auditWriter.Staged, e => e.ActionType == AuditActionType.UserCreated);
        Assert.Contains(_auditWriter.Staged, e => e.ActionType == AuditActionType.RoleAssigned);
    }

    [Fact]
    public async Task CreateUser_WithZeroRoles_Fails()
    {
        var result = await CreateUseCase().ExecuteAsync(Request(roles: []));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.role_required", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_WithANulCharacterInARoleCode_FailsWithoutTouchingTheStore()
    {
        var role = await CreateUseCase().ExecuteAsync(Request(roles: ["Office\0Staff"]));

        Assert.Equal("users.role_unknown", role.Error!.Code);
        Assert.False(_unitOfWork.Committed);
    }

    [Fact]
    public async Task CreateUser_WithDuplicateRoleCodes_Fails()
    {
        var result = await CreateUseCase().ExecuteAsync(Request(roles: ["OfficeStaff", "officestaff"]));

        Assert.False(result.IsSuccess);
        Assert.Equal("validation.failed", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_WithAnUnknownRole_Fails()
    {
        var result = await CreateUseCase().ExecuteAsync(Request(roles: ["NoSuchRole"]));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.role_unknown", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_WithADeprecatedRole_Fails()
    {
        var deprecated = new Role(new RoleCode("Retired"), RoleStatus.Active);
        deprecated.Deprecate();
        _roles.Seed(deprecated);

        var result = await CreateUseCase().ExecuteAsync(Request(roles: ["Retired"]));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.role_unknown", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_WithALoginIdentifierAlreadyTaken_Fails()
    {
        _users.Seed(new User(new LoginIdentifier("office.sara"), UserStatus.Active, "hash", mustChangePassword: false));

        var result = await CreateUseCase().ExecuteAsync(Request(login: "Office.Sara"));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.login_identifier_taken", result.Error!.Code);
        Assert.Empty(_issuances.All);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithoutPartyId_Fails()
    {
        var result = await CreateUseCase().ExecuteAsync(Request(login: "eng.ahmed", roles: ["Engineer"]));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.party_required", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_ForANonEngineerRole_WithAPartyId_Fails()
    {
        var party = SeedValidEngineerParty();

        var result = await CreateUseCase().ExecuteAsync(Request(partyId: party.Id));

        Assert.False(result.IsSuccess);
        Assert.Equal("validation.failed", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithAValidUnlinkedParty_LinksItInTheSameTransaction()
    {
        var party = SeedValidEngineerParty();

        var result = await CreateUseCase().ExecuteAsync(Request(login: "eng.ahmed", roles: ["Engineer"], partyId: party.Id));

        Assert.True(result.IsSuccess);
        var user = await _users.FindByIdAsync(result.Value!.UserId);
        Assert.Equal(party.Id, user!.PartyLink!.PartyId);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithAnUnknownParty_Fails()
    {
        var result = await CreateUseCase().ExecuteAsync(Request(login: "eng.ahmed", roles: ["Engineer"], partyId: Guid.CreateVersion7()));

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

        var result = await CreateUseCase().ExecuteAsync(Request(login: "eng.ahmed", roles: ["Engineer"], partyId: party.Id));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.party_invalid", result.Error!.Code);
        Assert.Equal("not_active", result.Error.Message);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithAPartyLackingAnActiveEngineerRole_Fails()
    {
        var party = new Party("Ahmed", PartyStatus.Active);
        _parties.Seed(party);

        var result = await CreateUseCase().ExecuteAsync(Request(login: "eng.ahmed", roles: ["Engineer"], partyId: party.Id));

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

        var result = await CreateUseCase().ExecuteAsync(Request(login: "eng.ahmed", roles: ["Engineer"], partyId: party.Id));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.party_invalid", result.Error!.Code);
        Assert.Equal("no_active_engineer_role", result.Error.Message);
    }

    [Fact]
    public async Task CreateUser_ForEngineer_WithAPartyAlreadyLinkedToAUser_Fails()
    {
        var party = SeedValidEngineerParty();
        _parties.MarkLinked(party.Id);

        var result = await CreateUseCase().ExecuteAsync(Request(login: "eng.ahmed", roles: ["Engineer"], partyId: party.Id));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.party_already_linked", result.Error!.Code);
    }

    [Fact]
    public async Task CreateUser_ARejectedRequest_NeverOpensATransaction()
    {
        await CreateUseCase().ExecuteAsync(Request(roles: []));

        Assert.False(_unitOfWork.TransactionOpen);
        Assert.False(_unitOfWork.Committed);
    }

    private sealed class FixedBusinessDateProvider : IBusinessDateProvider
    {
        private readonly DateOnly _today;

        public FixedBusinessDateProvider(DateOnly today) => _today = today;

        public DateOnly Today() => _today;
    }

    // ---- audit ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateUser_AuditEvents_CarryActorSourceCorrelationAndTheirSnapshots_AndNeverASecret()
    {
        var result = await CreateUseCase().ExecuteAsync(Request(correlation: "corr-create"));

        var userId = result.Value!.UserId;
        var user = await _users.FindByIdAsync(userId);
        var assignment = Assert.Single(user!.Roles);
        var issuance = Assert.Single(_issuances.All);

        // OC-6 resolved: ActorId has no active role in this fake, so the confirmed rule yields null here.
        var created = Assert.Single(_auditWriter.Staged, e => e.ActionType == AuditActionType.UserCreated);
        AuditAssert.Event(
            created, AuditActionType.UserCreated, ActorId, userId, "corr-create", AuditSource.Web, _clock.UtcNow,
            actorRoleCodeStillPendingOc6: false, expectedActorRoleCode: null);
        AuditAssert.SnapshotKeys(created, "credential_issuance_id", "consent_affirmed");
        var createdSnapshot = AuditAssert.SnapshotOf(created);
        Assert.Equal(issuance.Id.ToString(), createdSnapshot["credential_issuance_id"]);
        Assert.Equal("true", createdSnapshot["consent_affirmed"]);

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
            AuditAssert.ContainsNone(auditEvent, GeneratedPassword, user.PasswordHash, "office.sara", Number, "1000000001");
        }
    }

    // Contract §14.1, owner decision OC-6: creating a user requires Users.Manage, so the rule resolves to the
    // active role through which the actor held it (several -> first by ordinal code order).
    [Fact]
    public async Task CreateUser_AuditEvents_ActorRoleCode_IsTheRoleGrantingUsersManage()
    {
        _accessReader.Set(
            ActorId, ["Admin", "Accountant"],
            new() { ["Admin"] = ["Users.Manage"], ["Accountant"] = ["Users.Manage"] });

        await CreateUseCase().ExecuteAsync(Request(login: "office.sara2", correlation: "corr-create"));

        var created = Assert.Single(_auditWriter.Staged, e => e.ActionType == AuditActionType.UserCreated);
        Assert.Equal("Accountant", created.ActorRoleCode);
        var assigned = Assert.Single(_auditWriter.Staged, e => e.ActionType == AuditActionType.RoleAssigned);
        Assert.Equal("Accountant", assigned.ActorRoleCode);
    }

    // No role grants Users.Manage directly (it came from a direct grant to the actor): the rule falls back to
    // the actor's first active role.
    [Fact]
    public async Task CreateUser_AuditEvents_ActorRoleCode_FallsBackToTheFirstActiveRole_WhenNoRoleGrantsUsersManage()
    {
        _accessReader.Set(ActorId, ["OfficeStaff", "Engineer"], direct: ["Users.Manage"]);

        await CreateUseCase().ExecuteAsync(Request(login: "office.sara3", correlation: "corr-create"));

        var created = Assert.Single(_auditWriter.Staged, e => e.ActionType == AuditActionType.UserCreated);
        Assert.Equal("Engineer", created.ActorRoleCode);
    }

    [Fact]
    public async Task CreateUser_WithSeveralRoles_StagesOneRoleAssignedEventPerRoleEachWithItsOwnSnapshot()
    {
        var party = SeedValidEngineerParty();

        var result = await CreateUseCase().ExecuteAsync(
            Request(login: "eng.ahmed", roles: ["Engineer", "OfficeStaff"], partyId: party.Id, correlation: "corr-multi"));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, _auditWriter.Staged.Count);
        var snapshots = _auditWriter.Staged
            .Where(e => e.ActionType == AuditActionType.RoleAssigned)
            .Select(AuditAssert.SnapshotOf)
            .ToList();
        Assert.Equal(new[] { "Engineer", "OfficeStaff" }, snapshots.Select(s => s["role_code"]).OrderBy(c => c));
        Assert.Equal(2, snapshots.Select(s => s["user_role_id"]).Distinct().Count());

        // OC-6: the actor's role is resolved once and reused for every event of this request.
        Assert.Single(_auditWriter.Staged.Select(e => e.ActorRoleCode).Distinct());
    }
}
