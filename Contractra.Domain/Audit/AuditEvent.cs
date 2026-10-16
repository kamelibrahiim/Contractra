using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Contractra.Domain.Audit;

/// <summary>
/// One immutable audit record (business rules ح-185…ح-191; auth contract v1.5
/// §14; repository ADR 0006 §4–§5). Created only through <see cref="ForUser"/> and
/// <see cref="ForSystem"/>, which enforce every rule that does not need the
/// database; the table repeats them as CHECK constraints. There is no method that
/// changes an event, and the table rejects UPDATE, DELETE and TRUNCATE.
///
/// The time comes from the caller's application clock in UTC (ADR 0006 E26). The
/// Domain never generates a correlation id (generated at the HTTP boundary and per
/// seeder or command run) and never accepts a password, hash, reset code, token or
/// login identifier: the only free-form values are the validated reason and the
/// snapshot values of the approved keys.
/// </summary>
public sealed partial class AuditEvent
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxReasonLength = 1000;
    public const int MaxIdempotencyKeyLength = 256;
    public const int MaxSnapshotBytes = 8192;
    public const int MaxSnapshotValueLength = 256;

    public const string ActiveStatus = "Active";
    public const string DisabledStatus = "Disabled";

    // Allowed snapshot keys per action type (ADR 0006 §5.2). Anything else is refused.
    private static readonly string[] RoleSnapshotKeys =
        ["user_role_id", "role_id", "role_code", "assigned_at", "ended_at"];

    private static readonly string[] PermissionSnapshotKeys =
        ["user_permission_id", "permission_id", "permission_code", "granted_at", "revoked_at"];

    private static readonly string[] ResetTicketSnapshotKeys =
        ["password_reset_ticket_id", "issued_by_user_id", "channel", "issued_at", "expires_at", "consumed_at"];

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public AuditActorType ActorType { get; private set; }

    public Guid? ActorUserId { get; private set; }

    /// <summary>The role under which the user acted; null for System, and for a user with no active role.</summary>
    public string? ActorRoleCode { get; private set; }

    public AuditActionType ActionType { get; private set; }

    public string EntityType { get; private set; } = null!;

    public Guid EntityId { get; private set; }

    // Not populated by any factory yet (ADR 0006 E31, §11): the database keeps the
    // business document number, company, operation and site empty until defined.
    public string? BusinessDocumentNumber { get; private set; }

    public Guid? ManagedCompanyId { get; private set; }

    public Guid? OperationId { get; private set; }

    public Guid? SiteId { get; private set; }

    public string? PreviousStatus { get; private set; }

    public string? NewStatus { get; private set; }

    public string? Reason { get; private set; }

    // Not populated yet: its meaning is undefined (ADR 0006 §11, E32).
    public Guid? ApprovedByUserId { get; private set; }

    public string CorrelationId { get; private set; } = null!;

    // Not written in Sprint 2 (ADR 0006 E33).
    public string? CausationId { get; private set; }

    public AuditSource Source { get; private set; }

    /// <summary><c>&lt;action_type&gt;:&lt;operation key&gt;[:&lt;sub-identifier&gt;]</c> (ADR 0006 E30), or null.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>JSON object of approved, non-sensitive context keys (ADR 0006 §5.2), or null.</summary>
    public string? Snapshot { get; private set; }

    public int SchemaVersion { get; private set; }

    private AuditEvent()
    {
    }

    /// <summary>An action performed by a signed-in user.</summary>
    /// <param name="actorRoleCode">The role the user acted under; null only when the user holds no active role.</param>
    /// <param name="idempotencyOperationKey">
    /// The operation key (not the full key); required when <paramref name="source"/> is
    /// <see cref="AuditSource.OfflineSync"/>, <see cref="AuditSource.Import"/> or
    /// <see cref="AuditSource.SystemJob"/>.
    /// </param>
    /// <param name="idempotencySubIdentifier">
    /// Mandatory when one operation produces several events of the same action type.
    /// </param>
    public static AuditEvent ForUser(
        AuditActionType actionType,
        Guid actorUserId,
        string? actorRoleCode,
        string entityType,
        Guid entityId,
        DateTimeOffset occurredAtUtc,
        string correlationId,
        AuditSource source,
        string? reason = null,
        string? previousStatus = null,
        string? newStatus = null,
        IReadOnlyDictionary<string, string?>? snapshot = null,
        string? idempotencyOperationKey = null,
        string? idempotencySubIdentifier = null)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("A user event names its user.", nameof(actorUserId));
        }

        if (actorRoleCode is not null)
        {
            ValidateTrimmedNotEmpty(actorRoleCode, nameof(actorRoleCode));
        }

        return Create(
            AuditActorType.User, actorUserId, actorRoleCode, actionType, entityType, entityId,
            occurredAtUtc, correlationId, source, reason, previousStatus, newStatus, snapshot,
            idempotencyOperationKey, idempotencySubIdentifier);
    }

    /// <summary>
    /// An action performed by the system itself: no user, no role, source
    /// <see cref="AuditSource.SystemJob"/>. The operation key is always required
    /// (a SystemJob event is a job record under integrity rule PRV-08). The reason is
    /// required for operator-command events (<see cref="AuditActionType.PasswordResetIssued"/>
    /// and <see cref="AuditActionType.BootstrapPasswordReset"/>); the seeder passes a
    /// fixed descriptive reason (ADR 0006 E34).
    /// </summary>
    public static AuditEvent ForSystem(
        AuditActionType actionType,
        string entityType,
        Guid entityId,
        DateTimeOffset occurredAtUtc,
        string correlationId,
        string idempotencyOperationKey,
        string? reason = null,
        string? previousStatus = null,
        string? newStatus = null,
        IReadOnlyDictionary<string, string?>? snapshot = null,
        string? idempotencySubIdentifier = null)
    {
        if (actionType is AuditActionType.PasswordResetIssued or AuditActionType.BootstrapPasswordReset
            && reason is null)
        {
            throw new ArgumentException(
                $"A System {actionType} event is an operator command and requires a reason.", nameof(reason));
        }

        return Create(
            AuditActorType.System, actorUserId: null, actorRoleCode: null, actionType, entityType, entityId,
            occurredAtUtc, correlationId, AuditSource.SystemJob, reason, previousStatus, newStatus, snapshot,
            idempotencyOperationKey, idempotencySubIdentifier);
    }

    private static AuditEvent Create(
        AuditActorType actorType,
        Guid? actorUserId,
        string? actorRoleCode,
        AuditActionType actionType,
        string entityType,
        Guid entityId,
        DateTimeOffset occurredAtUtc,
        string correlationId,
        AuditSource source,
        string? reason,
        string? previousStatus,
        string? newStatus,
        IReadOnlyDictionary<string, string?>? snapshot,
        string? operationKey,
        string? subIdentifier)
    {
        if (!Enum.IsDefined(actionType))
        {
            throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Unknown audit action type.");
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown audit source.");
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC (offset 00:00).", nameof(occurredAtUtc));
        }

        if (entityType is null || !EntityTypePattern().IsMatch(entityType))
        {
            throw new ArgumentException(
                "The entity type must start with an upper-case letter followed by at most 63 letters or digits.", nameof(entityType));
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("An audit event names the entity it concerns.", nameof(entityId));
        }

        // Contract §13: 1-64 characters from A-Z a-z 0-9 - _ . Generated elsewhere, never here.
        if (correlationId is null || !CorrelationIdPattern().IsMatch(correlationId))
        {
            throw new ArgumentException(
                "The correlation id must be 1-64 characters from A-Z a-z 0-9 - _ .", nameof(correlationId));
        }

        if (reason is not null)
        {
            ValidateTrimmedNotEmpty(reason, nameof(reason));
            if (reason.Length > MaxReasonLength)
            {
                throw new ArgumentException($"The reason cannot exceed {MaxReasonLength} characters.", nameof(reason));
            }
        }

        ValidateStatuses(actionType, previousStatus, newStatus);

        return new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            OccurredAtUtc = occurredAtUtc,
            ActorType = actorType,
            ActorUserId = actorUserId,
            ActorRoleCode = actorRoleCode,
            ActionType = actionType,
            EntityType = entityType,
            EntityId = entityId,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Reason = reason,
            CorrelationId = correlationId,
            Source = source,
            IdempotencyKey = BuildIdempotencyKey(actionType, source, operationKey, subIdentifier),
            Snapshot = BuildSnapshot(actionType, snapshot),
            SchemaVersion = CurrentSchemaVersion,
        };
    }

    /// <summary>
    /// <c>&lt;action_type&gt;:&lt;operation key&gt;[:&lt;sub-identifier&gt;]</c>, always composed
    /// from the event's own action type so two action types never produce the same key
    /// (ADR 0006 E30, §5.3). Null when no key is given and the source does not require one.
    /// </summary>
    public static string? BuildIdempotencyKey(
        AuditActionType actionType,
        AuditSource source,
        string? operationKey,
        string? subIdentifier)
    {
        var required = source is AuditSource.OfflineSync or AuditSource.Import or AuditSource.SystemJob;

        if (operationKey is null)
        {
            if (subIdentifier is not null)
            {
                throw new ArgumentException("A sub-identifier needs an operation key.", nameof(subIdentifier));
            }

            if (required)
            {
                throw new ArgumentException(
                    $"An event from source {source} requires an idempotency key (ح-188).", nameof(operationKey));
            }

            return null;
        }

        ValidateKeyPart(operationKey, nameof(operationKey));
        var key = $"{actionType}:{operationKey}";

        if (subIdentifier is not null)
        {
            ValidateKeyPart(subIdentifier, nameof(subIdentifier));
            key += $":{subIdentifier}";
        }

        if (key.Length > MaxIdempotencyKeyLength)
        {
            throw new ArgumentException(
                $"The idempotency key cannot exceed {MaxIdempotencyKeyLength} characters.", nameof(operationKey));
        }

        return key;
    }

    private static void ValidateKeyPart(string part, string parameterName)
    {
        if (part.Length == 0 || part.Any(c => c < '!' || c > '~'))
        {
            throw new ArgumentException(
                "A key part must be non-empty printable ASCII without spaces.", parameterName);
        }
    }

    private static void ValidateStatuses(AuditActionType actionType, string? previousStatus, string? newStatus)
    {
        if ((previousStatus is null) != (newStatus is null))
        {
            throw new ArgumentException("Previous and new status are both set or both null.", nameof(newStatus));
        }

        if (previousStatus is not null && newStatus is not null)
        {
            ValidateTrimmedNotEmpty(previousStatus, nameof(previousStatus));
            ValidateTrimmedNotEmpty(newStatus, nameof(newStatus));

            if (previousStatus == newStatus)
            {
                throw new ArgumentException("A status change changes the status.", nameof(newStatus));
            }
        }

        // Which events must carry a status pair (ADR 0006 E35): the two user status changes.
        string? expectedPrevious = null;
        string? expectedNew = null;
        switch (actionType)
        {
            case AuditActionType.UserDisabled:
                expectedPrevious = ActiveStatus;
                expectedNew = DisabledStatus;
                break;
            case AuditActionType.UserReactivated:
                expectedPrevious = DisabledStatus;
                expectedNew = ActiveStatus;
                break;
        }

        if (expectedPrevious is not null && (previousStatus != expectedPrevious || newStatus != expectedNew))
        {
            throw new ArgumentException(
                $"{actionType} records the status change {expectedPrevious} → {expectedNew}.", nameof(newStatus));
        }
    }

    private static string? BuildSnapshot(AuditActionType actionType, IReadOnlyDictionary<string, string?>? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var allowed = actionType switch
        {
            AuditActionType.RoleAssigned or AuditActionType.RoleEnded => RoleSnapshotKeys,
            AuditActionType.PermissionGranted or AuditActionType.PermissionRevoked
                or AuditActionType.PayrollViewGranted or AuditActionType.PayrollViewRevoked => PermissionSnapshotKeys,
            AuditActionType.PasswordResetIssued or AuditActionType.PasswordResetCompleted => ResetTicketSnapshotKeys,
            _ => throw new ArgumentException($"A {actionType} event carries no snapshot.", nameof(snapshot)),
        };

        if (snapshot.Count == 0)
        {
            throw new ArgumentException("A snapshot has at least one key.", nameof(snapshot));
        }

        foreach (var (key, value) in snapshot)
        {
            if (!allowed.Contains(key, StringComparer.Ordinal))
            {
                throw new ArgumentException($"'{key}' is not an approved snapshot key for {actionType}.", nameof(snapshot));
            }

            if (value is not null && (value.Length == 0 || value.Length > MaxSnapshotValueLength))
            {
                throw new ArgumentException(
                    $"Snapshot value '{key}' must be null or 1-{MaxSnapshotValueLength} characters.", nameof(snapshot));
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var key in allowed.Where(snapshot.ContainsKey))
            {
                writer.WriteString(key, snapshot[key]);
            }

            writer.WriteEndObject();
        }

        if (stream.Length > MaxSnapshotBytes)
        {
            throw new ArgumentException($"A snapshot cannot exceed {MaxSnapshotBytes} bytes.", nameof(snapshot));
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void ValidateTrimmedNotEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be empty or whitespace.", parameterName);
        }

        // Rejected rather than trimmed, as for the Identity value objects.
        if (value.Length != value.Trim().Length)
        {
            throw new ArgumentException("The value cannot have leading or trailing whitespace.", parameterName);
        }
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]{0,63}$")]
    private static partial Regex EntityTypePattern();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex CorrelationIdPattern();
}
