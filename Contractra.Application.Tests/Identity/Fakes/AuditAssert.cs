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
    /// action. <c>actor_role_code</c> defaults to the still-pending-OC-6
    /// interim state (see <see cref="ActorRoleCodeIsUnresolvedPendingOc6"/>);
    /// pass <paramref name="expectedActorRoleCode"/> for an event whose use
    /// case now resolves it under the confirmed OC-6 rule.
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
        string? newStatus = null,
        bool actorRoleCodeStillPendingOc6 = true,
        string? expectedActorRoleCode = null)
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
        if (actorRoleCodeStillPendingOc6)
        {
            ActorRoleCodeIsUnresolvedPendingOc6(auditEvent);
        }
        else
        {
            Assert.Equal(expectedActorRoleCode, auditEvent.ActorRoleCode);
        }
    }

    /// <summary>
    /// NOT a statement that null is correct. OC-6 (contract §19) confirmed the
    /// §14.1 rule (<see cref="Contractra.Application.Identity.Auth.ActorRoleCodeRule"/>),
    /// but the Slice 4 users-endpoint use cases (create, disable, reactivate,
    /// issue-reset, initial re-issue - contract §9, Slice 7) still write null:
    /// they are permission-gated (<c>Users.Manage</c>) and have not yet been
    /// updated to resolve the actor's role under that permission. This
    /// assertion pins that remaining interim state so it cannot change
    /// unnoticed; update it, and these use cases, together when that work is done.
    /// </summary>
    public static void ActorRoleCodeIsUnresolvedPendingOc6(AuditEvent auditEvent) =>
        Assert.True(
            auditEvent.ActorRoleCode is null,
            "Interim state: this use case does not yet resolve actor_role_code under the confirmed OC-6 rule.");

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
