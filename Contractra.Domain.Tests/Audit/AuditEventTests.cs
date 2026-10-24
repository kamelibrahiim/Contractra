using System.Text.Json;
using Contractra.Domain.Audit;

namespace Contractra.Domain.Tests.Audit;

/// <summary>
/// The Domain rules of an audit event (business rules ح-185…ح-191; auth contract
/// v1.5 §13, §14; repository ADR 0006 §4, §5, §9).
/// </summary>
public class AuditEventTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid EntityId = Guid.CreateVersion7();
    private const string Correlation = "0193a5c2-7d1e-7000-8000-000000000001";

    private static AuditEvent UserEvent(
        AuditActionType action = AuditActionType.PasswordChanged,
        AuditSource source = AuditSource.Web,
        string? roleCode = "Admin",
        string? reason = null,
        string? previous = null,
        string? next = null,
        IReadOnlyDictionary<string, string?>? snapshot = null,
        string? operationKey = null,
        string? subIdentifier = null) =>
        AuditEvent.ForUser(
            action, UserId, roleCode, "User", EntityId, T0, Correlation, source,
            reason, previous, next, snapshot, operationKey, subIdentifier);

    private static AuditEvent SystemEvent(
        AuditActionType action = AuditActionType.UserCreated,
        string operationKey = "bootstrap:abc",
        string? reason = null,
        string? subIdentifier = null) =>
        AuditEvent.ForSystem(action, "User", EntityId, T0, Correlation, operationKey, reason, idempotencySubIdentifier: subIdentifier);

    // ---- catalogs --------------------------------------------------------

    // Contract §22 I3: "a later rename adds a value and keeps the old one
    // valid" - ThrottleCleared (§22 M5, migration AddThrottleClearedAuditAction)
    // is exactly that sanctioned growth, the 16th code, appended after the
    // original 15 rather than replacing or reordering any of them.
    [Fact]
    public void ActionCatalog_IsTheSixteenContractCodes_WithTheOwnerSelectedNames()
    {
        Assert.Equal(
            [
                "UserCreated", "UserDisabled", "UserReactivated", "RoleAssigned", "RoleEnded",
                "PermissionGranted", "PermissionRevoked", "PayrollViewGranted", "PayrollViewRevoked",
                "PasswordChanged", "PasswordResetIssued", "PasswordResetCompleted",
                "InitialPasswordReissued", "BootstrapPasswordReset", "AccessDenied", "ThrottleCleared",
            ],
            Enum.GetNames<AuditActionType>());
    }

    [Theory]
    [InlineData("PasswordReset")]
    [InlineData("LoginFailed")]
    [InlineData("UserDeactivated")]
    [InlineData("RoleChanged")]
    public void ActionCatalog_ExcludesWithdrawnAndSerilogOnlyNames(string name) =>
        Assert.DoesNotContain(name, Enum.GetNames<AuditActionType>());

    [Fact]
    public void SourceCatalog_UsesEmployeeAppAndNotPwa()
    {
        Assert.Equal(["Web", "EmployeeApp", "OfflineSync", "Import", "SystemJob"], Enum.GetNames<AuditSource>());
        Assert.DoesNotContain("PWA", Enum.GetNames<AuditSource>());
    }

    [Fact]
    public void ActorCatalog_IsUserAndSystem() =>
        Assert.Equal(["User", "System"], Enum.GetNames<AuditActorType>());

    // ---- ForUser ---------------------------------------------------------

    [Fact]
    public void ForUser_RecordsTheActionExactly()
    {
        var e = UserEvent(reason: "Changed by the owner");

        Assert.NotEqual(Guid.Empty, e.Id);
        Assert.Equal(7, e.Id.Version);
        Assert.Equal(T0, e.OccurredAtUtc);
        Assert.Equal(AuditActorType.User, e.ActorType);
        Assert.Equal(UserId, e.ActorUserId);
        Assert.Equal("Admin", e.ActorRoleCode);
        Assert.Equal(AuditActionType.PasswordChanged, e.ActionType);
        Assert.Equal("User", e.EntityType);
        Assert.Equal(EntityId, e.EntityId);
        Assert.Equal("Changed by the owner", e.Reason);
        Assert.Equal(Correlation, e.CorrelationId);
        Assert.Equal(AuditSource.Web, e.Source);
        Assert.Equal(1, e.SchemaVersion);
        Assert.Null(e.IdempotencyKey);
        Assert.Null(e.Snapshot);
        Assert.Null(e.PreviousStatus);
        Assert.Null(e.NewStatus);
    }

    [Fact]
    public void ForUser_DoesNotPopulateTheUndefinedContextFields()
    {
        var e = UserEvent();

        Assert.Null(e.ManagedCompanyId);
        Assert.Null(e.OperationId);
        Assert.Null(e.SiteId);
        Assert.Null(e.ApprovedByUserId);
        Assert.Null(e.CausationId);
        Assert.Null(e.BusinessDocumentNumber);
    }

    [Fact]
    public void ForUser_AllowsNoRoleCode_ForAUserWithoutAnActiveRole() =>
        Assert.Null(UserEvent(roleCode: null).ActorRoleCode);

    [Fact]
    public void ForUser_EachEventGetsItsOwnId() =>
        Assert.NotEqual(UserEvent().Id, UserEvent().Id);

    [Fact]
    public void ForUser_RejectsAnEmptyUserId() =>
        Assert.Throws<ArgumentException>(() =>
            AuditEvent.ForUser(AuditActionType.PasswordChanged, Guid.Empty, "Admin", "User", EntityId, T0, Correlation, AuditSource.Web));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" Admin")]
    [InlineData("Admin ")]
    public void ForUser_RejectsABlankOrPaddedRoleCode(string roleCode) =>
        Assert.Throws<ArgumentException>(() => UserEvent(roleCode: roleCode));

    // ---- ForSystem -------------------------------------------------------

    [Fact]
    public void ForSystem_HasNoUserNoRoleAndTheSystemJobSource()
    {
        var e = SystemEvent();

        Assert.Equal(AuditActorType.System, e.ActorType);
        Assert.Null(e.ActorUserId);
        Assert.Null(e.ActorRoleCode);
        Assert.Equal(AuditSource.SystemJob, e.Source);
    }

    [Theory]
    [InlineData(AuditActionType.PasswordResetIssued)]
    [InlineData(AuditActionType.BootstrapPasswordReset)]
    [InlineData(AuditActionType.ThrottleCleared)]
    public void ForSystem_RequiresAReason_ForOperatorCommandEvents(AuditActionType action)
    {
        Assert.Throws<ArgumentException>(() => SystemEvent(action));
        Assert.Equal("Operator reset after a lost password", SystemEvent(action, reason: "Operator reset after a lost password").Reason);
    }

    [Theory]
    [InlineData(AuditActionType.UserCreated)]
    [InlineData(AuditActionType.RoleAssigned)]
    [InlineData(AuditActionType.PayrollViewGranted)]
    public void ForSystem_NeedsNoReason_ForOtherEvents_WhichTheSeederMayStillGive(AuditActionType action)
    {
        Assert.Null(SystemEvent(action).Reason);
        Assert.Equal("Bootstrap administrator seeding", SystemEvent(action, reason: "Bootstrap administrator seeding").Reason);
    }

    [Fact]
    public void ForSystem_RequiresAnOperationKey() =>
        Assert.Throws<ArgumentException>(() => SystemEvent(operationKey: null!));

    // ---- time, entity, correlation --------------------------------------

    [Fact]
    public void Time_MustBeUtc_AndIsRejectedNotConverted() =>
        Assert.Throws<ArgumentException>(() =>
            AuditEvent.ForUser(AuditActionType.PasswordChanged, UserId, "Admin", "User", EntityId, T0.ToOffset(TimeSpan.FromHours(2)), Correlation, AuditSource.Web));

    [Theory]
    [InlineData("")]
    [InlineData("user")]
    [InlineData("1User")]
    [InlineData("User Role")]
    [InlineData("User-Role")]
    public void EntityType_MustBeAPascalCaseName(string entityType) =>
        Assert.Throws<ArgumentException>(() =>
            AuditEvent.ForUser(AuditActionType.PasswordChanged, UserId, "Admin", entityType, EntityId, T0, Correlation, AuditSource.Web));

    [Fact]
    public void EntityId_MustNotBeEmpty() =>
        Assert.Throws<ArgumentException>(() =>
            AuditEvent.ForUser(AuditActionType.PasswordChanged, UserId, "Admin", "User", Guid.Empty, T0, Correlation, AuditSource.Web));

    [Theory]
    [InlineData("a")]
    [InlineData("A-b_c.1")]
    [InlineData("0193a5c2-7d1e-7000-8000-000000000001")]
    public void CorrelationId_AcceptsTheContractCharactersAndLength(string id) =>
        Assert.Equal(id, AuditEvent.ForUser(AuditActionType.PasswordChanged, UserId, "Admin", "User", EntityId, T0, id, AuditSource.Web).CorrelationId);

    [Fact]
    public void CorrelationId_Accepts64Characters_AndRejects65()
    {
        Assert.Equal(64, UserEventWith(new string('a', 64)).CorrelationId.Length);
        Assert.Throws<ArgumentException>(() => UserEventWith(new string('a', 65)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("slash/")]
    [InlineData("colon:")]
    [InlineData("é")]
    [InlineData("new\nline")]
    public void CorrelationId_RejectsAnythingElse_AndNeverGeneratesOne(string id) =>
        Assert.Throws<ArgumentException>(() => UserEventWith(id));

    [Fact]
    public void CorrelationId_NullIsRejected_NotReplaced() =>
        Assert.Throws<ArgumentException>(() => UserEventWith(null!));

    private static AuditEvent UserEventWith(string correlationId) =>
        AuditEvent.ForUser(AuditActionType.PasswordChanged, UserId, "Admin", "User", EntityId, T0, correlationId, AuditSource.Web);

    // ---- reason ----------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" padded")]
    [InlineData("padded ")]
    public void Reason_RejectsBlankOrPaddedText(string reason) =>
        Assert.Throws<ArgumentException>(() => UserEvent(reason: reason));

    [Fact]
    public void Reason_Accepts1000Characters_AndRejects1001()
    {
        Assert.Equal(1000, UserEvent(reason: new string('r', 1000)).Reason!.Length);
        Assert.Throws<ArgumentException>(() => UserEvent(reason: new string('r', 1001)));
    }

    // ---- statuses --------------------------------------------------------

    [Fact]
    public void Status_BothOrNeither()
    {
        Assert.Throws<ArgumentException>(() => UserEvent(previous: "Active"));
        Assert.Throws<ArgumentException>(() => UserEvent(next: "Disabled"));
    }

    [Fact]
    public void Status_MustChange() =>
        Assert.Throws<ArgumentException>(() => UserEvent(previous: "Active", next: "Active"));

    [Theory]
    [InlineData("", "Disabled")]
    [InlineData("Active", " ")]
    [InlineData(" Active", "Disabled")]
    public void Status_RejectsBlankOrPaddedValues(string previous, string next) =>
        Assert.Throws<ArgumentException>(() => UserEvent(previous: previous, next: next));

    [Fact]
    public void UserDisabled_RecordsActiveToDisabled_AndRequiresIt()
    {
        var e = UserEvent(AuditActionType.UserDisabled, previous: "Active", next: "Disabled");

        Assert.Equal("Active", e.PreviousStatus);
        Assert.Equal("Disabled", e.NewStatus);
        Assert.Throws<ArgumentException>(() => UserEvent(AuditActionType.UserDisabled));
        Assert.Throws<ArgumentException>(() => UserEvent(AuditActionType.UserDisabled, previous: "Disabled", next: "Active"));
    }

    [Fact]
    public void UserReactivated_RecordsDisabledToActive_AndRequiresIt()
    {
        var e = UserEvent(AuditActionType.UserReactivated, previous: "Disabled", next: "Active");

        Assert.Equal("Disabled", e.PreviousStatus);
        Assert.Equal("Active", e.NewStatus);
        Assert.Throws<ArgumentException>(() => UserEvent(AuditActionType.UserReactivated));
        Assert.Throws<ArgumentException>(() => UserEvent(AuditActionType.UserReactivated, previous: "Active", next: "Disabled"));
    }

    // ---- idempotency key -------------------------------------------------

    [Fact]
    public void IdempotencyKey_IsComposedFromTheEventsOwnActionType()
    {
        Assert.Equal("UserCreated:bootstrap:abc", SystemEvent(AuditActionType.UserCreated, "bootstrap:abc").IdempotencyKey);
        Assert.Equal("RoleAssigned:bootstrap:abc", SystemEvent(AuditActionType.RoleAssigned, "bootstrap:abc").IdempotencyKey);
        Assert.Equal(
            "PasswordResetIssued:command:run-1",
            SystemEvent(AuditActionType.PasswordResetIssued, "command:run-1", reason: "Operator").IdempotencyKey);
    }

    [Fact]
    public void IdempotencyKey_OneOperationWithSeveralActionTypes_NeverCollides()
    {
        // The seeder writes three events under one operation key.
        var keys = new[]
        {
            SystemEvent(AuditActionType.UserCreated, "bootstrap:abc").IdempotencyKey,
            SystemEvent(AuditActionType.RoleAssigned, "bootstrap:abc").IdempotencyKey,
            SystemEvent(AuditActionType.PayrollViewGranted, "bootstrap:abc").IdempotencyKey,
        };

        Assert.Equal(3, keys.Distinct().Count());
    }

    [Fact]
    public void IdempotencyKey_SameActionTypeTwice_NeedsASubIdentifierToBeDistinct()
    {
        Assert.Equal(
            SystemEvent(AuditActionType.RoleAssigned, "op").IdempotencyKey,
            SystemEvent(AuditActionType.RoleAssigned, "op").IdempotencyKey);

        var first = SystemEvent(AuditActionType.RoleAssigned, "op", subIdentifier: "assignment-1").IdempotencyKey;
        var second = SystemEvent(AuditActionType.RoleAssigned, "op", subIdentifier: "assignment-2").IdempotencyKey;

        Assert.Equal("RoleAssigned:op:assignment-1", first);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(AuditSource.OfflineSync)]
    [InlineData(AuditSource.Import)]
    [InlineData(AuditSource.SystemJob)]
    public void IdempotencyKey_IsRequired_ForSyncImportAndJobSources(AuditSource source)
    {
        Assert.Throws<ArgumentException>(() => UserEvent(source: source));
        Assert.Equal($"PasswordChanged:k1", UserEvent(source: source, operationKey: "k1").IdempotencyKey);
    }

    [Theory]
    [InlineData(AuditSource.Web)]
    [InlineData(AuditSource.EmployeeApp)]
    public void IdempotencyKey_IsOptional_ForOnlineSources_AndUsedWhenGiven(AuditSource source)
    {
        Assert.Null(UserEvent(source: source).IdempotencyKey);
        Assert.Equal("PasswordChanged:op-1", UserEvent(source: source, operationKey: "op-1").IdempotencyKey);
    }

    [Fact]
    public void IdempotencyKey_SubIdentifierNeedsAnOperationKey() =>
        Assert.Throws<ArgumentException>(() => UserEvent(subIdentifier: "x"));

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("tab\t")]
    [InlineData("é")]
    public void IdempotencyKey_PartsMustBePrintableAsciiWithoutSpaces(string part)
    {
        Assert.Throws<ArgumentException>(() => UserEvent(operationKey: part));
        Assert.Throws<ArgumentException>(() => UserEvent(operationKey: "ok", subIdentifier: part));
    }

    [Fact]
    public void IdempotencyKey_FullKeyIsAtMost256Characters()
    {
        var maxOperation = new string('k', 256 - "PasswordChanged:".Length);

        Assert.Equal(256, UserEvent(operationKey: maxOperation).IdempotencyKey!.Length);
        Assert.Throws<ArgumentException>(() => UserEvent(operationKey: maxOperation + "k"));
    }

    // ---- snapshot --------------------------------------------------------

    [Fact]
    public void Snapshot_RoleEvents_AcceptTheApprovedKeys_AsAJsonObject()
    {
        var snapshot = new Dictionary<string, string?>
        {
            ["role_code"] = "Engineer",
            ["user_role_id"] = "0193a5c2-7d1e-7000-8000-0000000000aa",
            ["role_id"] = "0193a5c2-7d1e-7000-8000-0000000000bb",
            ["assigned_at"] = "2026-10-16T08:00:00.0000000+00:00",
            ["ended_at"] = null,
        };

        var e = UserEvent(AuditActionType.RoleAssigned, snapshot: snapshot);

        using var json = JsonDocument.Parse(e.Snapshot!);
        Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
        Assert.Equal("Engineer", json.RootElement.GetProperty("role_code").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("ended_at").ValueKind);
        Assert.Equal(5, json.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(AuditActionType.PermissionGranted, "permission_code")]
    [InlineData(AuditActionType.PermissionRevoked, "revoked_at")]
    [InlineData(AuditActionType.PayrollViewGranted, "user_permission_id")]
    [InlineData(AuditActionType.PayrollViewRevoked, "permission_id")]
    [InlineData(AuditActionType.PasswordResetIssued, "password_reset_ticket_id")]
    [InlineData(AuditActionType.PasswordResetCompleted, "consumed_at")]
    [InlineData(AuditActionType.RoleEnded, "ended_at")]
    public void Snapshot_AcceptsAnApprovedKeyOfItsActionType(AuditActionType action, string key)
    {
        var e = UserEvent(action, snapshot: new Dictionary<string, string?> { [key] = "value" });

        using var json = JsonDocument.Parse(e.Snapshot!);
        Assert.Equal("value", json.RootElement.GetProperty(key).GetString());
    }

    [Theory]
    [InlineData(AuditActionType.RoleAssigned, "permission_code")]
    [InlineData(AuditActionType.PermissionGranted, "role_code")]
    [InlineData(AuditActionType.PasswordResetIssued, "code_hash")]
    [InlineData(AuditActionType.PasswordResetCompleted, "reset_code")]
    [InlineData(AuditActionType.RoleAssigned, "password_hash")]
    [InlineData(AuditActionType.RoleAssigned, "login_identifier")]
    [InlineData(AuditActionType.RoleAssigned, "refresh_token")]
    [InlineData(AuditActionType.RoleAssigned, "Role_Code")]
    public void Snapshot_RefusesAnyKeyOutsideTheApprovedList(AuditActionType action, string key) =>
        Assert.Throws<ArgumentException>(() =>
            UserEvent(action, snapshot: new Dictionary<string, string?> { [key] = "value" }));

    [Theory]
    [InlineData(AuditActionType.UserCreated)]
    [InlineData(AuditActionType.UserDisabled)]
    [InlineData(AuditActionType.PasswordChanged)]
    [InlineData(AuditActionType.AccessDenied)]
    [InlineData(AuditActionType.InitialPasswordReissued)]
    public void Snapshot_IsRefused_OnActionTypesWithoutOne(AuditActionType action)
    {
        var previous = action == AuditActionType.UserDisabled ? "Active" : null;
        var next = action == AuditActionType.UserDisabled ? "Disabled" : null;

        Assert.Throws<ArgumentException>(() =>
            UserEvent(action, previous: previous, next: next, snapshot: new Dictionary<string, string?> { ["role_code"] = "x" }));
    }

    [Fact]
    public void Snapshot_EmptyOrOversizedValuesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => UserEvent(AuditActionType.RoleAssigned, snapshot: new Dictionary<string, string?>()));
        Assert.Throws<ArgumentException>(() =>
            UserEvent(AuditActionType.RoleAssigned, snapshot: new Dictionary<string, string?> { ["role_code"] = "" }));
        Assert.Throws<ArgumentException>(() =>
            UserEvent(AuditActionType.RoleAssigned, snapshot: new Dictionary<string, string?> { ["role_code"] = new string('x', 257) }));
        Assert.Equal(
            256,
            JsonDocument.Parse(UserEvent(AuditActionType.RoleAssigned, snapshot: new Dictionary<string, string?> { ["role_code"] = new string('x', 256) }).Snapshot!)
                .RootElement.GetProperty("role_code").GetString()!.Length);
    }

    [Fact]
    public void Snapshot_IsAtMost8KiB_ForEveryActionType()
    {
        var all = new Dictionary<string, string?>
        {
            ["password_reset_ticket_id"] = new string('a', 256),
            ["issued_by_user_id"] = new string('a', 256),
            ["channel"] = new string('a', 256),
            ["issued_at"] = new string('a', 256),
            ["expires_at"] = new string('a', 256),
            ["consumed_at"] = new string('a', 256),
        };

        Assert.True(UserEvent(AuditActionType.PasswordResetIssued, snapshot: all).Snapshot!.Length < AuditEvent.MaxSnapshotBytes);
    }

    // ---- the event cannot be changed after creation ---------------------

    [Fact]
    public void AuditEvent_HasNoPublicSetterOrMutator()
    {
        var type = typeof(AuditEvent);

        Assert.All(
            type.GetProperties(),
            property => Assert.True(property.SetMethod is null || !property.SetMethod.IsPublic, $"{property.Name} has a public setter"));
        Assert.Empty(type.GetConstructors());

        var publicInstanceMethods = type
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);
        Assert.Empty(publicInstanceMethods);
    }

    [Fact]
    public void AuditEvent_HasNoSecretBearingProperty()
    {
        var names = typeof(AuditEvent).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToArray();

        Assert.DoesNotContain(names, n => n.Contains("password") || n.Contains("token") || n.Contains("secret")
            || n.Contains("hash") || n.Contains("loginidentifier") || n.Contains("ipaddress") || n.Contains("useragent")
            || n.Contains("payload") || n.Contains("clienteventid"));
    }
}
