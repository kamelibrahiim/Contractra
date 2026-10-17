using System.Globalization;
using System.Text.Json;
using Contractra.Domain.Audit;

namespace Contractra.Application.Tests.Identity.Fakes;

/// <summary>
/// Shared audit assertions for the Slice 4 use-case tests (auth contract v1.5
/// §14, §14.1; repository ADR 0006 §5.2, §12).
/// </summary>
internal static class AuditAssert
{
    /// <summary>
    /// Every field the contract fixes for an Identity event staged by a user
    /// action. <c>actor_role_code</c> is asserted separately, see
    /// <see cref="ActorRoleCodeIsUnresolvedPendingOc6"/>.
    /// </summary>
    public static void Event(
        AuditEvent auditEvent,
        AuditActionType action,
        Guid actorUserId,
        Guid targetUserId,
        string correlationId,
        AuditSource source,
        DateTimeOffset occurredAtUtc,
        string? previousStatus = null,
        string? newStatus = null)
    {
        Assert.Equal(action, auditEvent.ActionType);
        Assert.Equal(AuditActorType.User, auditEvent.ActorType);
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("User", auditEvent.EntityType);
        Assert.Equal(targetUserId, auditEvent.EntityId);
        Assert.Equal(correlationId, auditEvent.CorrelationId);
        Assert.Equal(source, auditEvent.Source);
        Assert.Equal(occurredAtUtc, auditEvent.OccurredAtUtc);
        Assert.Equal(previousStatus, auditEvent.PreviousStatus);
        Assert.Equal(newStatus, auditEvent.NewStatus);
        Assert.Null(auditEvent.IdempotencyKey);
        Assert.Equal(1, auditEvent.SchemaVersion);
        ActorRoleCodeIsUnresolvedPendingOc6(auditEvent);
    }

    /// <summary>
    /// NOT a statement that null is correct. Contract v1.5 §14.1 says
    /// <c>actor_role_code</c> is the role the actor acted under, and §19 OC-6
    /// leaves that rule awaiting owner confirmation, so Slice 4 writes null
    /// everywhere. This one assertion pins that interim state so it cannot
    /// change unnoticed; when OC-6 is decided, change it here and add the
    /// role-resolution tests.
    /// </summary>
    public static void ActorRoleCodeIsUnresolvedPendingOc6(AuditEvent auditEvent) =>
        Assert.True(
            auditEvent.ActorRoleCode is null,
            "Interim state: actor_role_code is not resolved while contract §19 OC-6 is pending.");

    /// <summary>The event's snapshot (ADR 0006 §5.2) as key/value pairs.</summary>
    public static IReadOnlyDictionary<string, string?> SnapshotOf(AuditEvent auditEvent)
    {
        Assert.NotNull(auditEvent.Snapshot);
        using var document = JsonDocument.Parse(auditEvent.Snapshot!);
        var result = new Dictionary<string, string?>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString();
        }

        return result;
    }

    public static void SnapshotKeys(AuditEvent auditEvent, params string[] expectedKeys) =>
        Assert.Equal(expectedKeys.OrderBy(k => k), SnapshotOf(auditEvent).Keys.OrderBy(k => k));

    public static void SnapshotTime(IReadOnlyDictionary<string, string?> snapshot, string key, DateTimeOffset expected)
    {
        Assert.NotNull(snapshot[key]);
        Assert.Equal(expected, DateTimeOffset.Parse(snapshot[key]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    /// <summary>Nothing secret appears anywhere in the event (ADR 0006 §5.2 "never captured").</summary>
    public static void ContainsNone(AuditEvent auditEvent, params string?[] secrets)
    {
        var text = string.Join('|', auditEvent.Snapshot, auditEvent.Reason, auditEvent.CorrelationId, auditEvent.EntityType);
        foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
        {
            Assert.DoesNotContain(secret!, text);
        }
    }
}
