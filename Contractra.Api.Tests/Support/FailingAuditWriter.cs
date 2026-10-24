using Contractra.Application.Audit;
using Contractra.Domain.Audit;

namespace Contractra.Api.Tests.Support;

/// <summary>
/// Forces the same real database failure the existing Slice 5/6 Infrastructure
/// tests use (<c>BadActorAuditWriter</c>): stages the real event, then one more
/// valid-looking event whose actor matches no user, so the FK constraint
/// (<c>fk_audit_events_users_actor_user_id</c>) rejects the whole transaction's
/// SaveChanges. No production code is changed; this only decorates the real
/// <see cref="IAuditWriter"/> inside the test host.
/// </summary>
internal sealed class FailingAuditWriter : IAuditWriter
{
    private readonly IAuditWriter _inner;

    public FailingAuditWriter(IAuditWriter inner) => _inner = inner;

    public void Stage(AuditEvent auditEvent)
    {
        _inner.Stage(auditEvent);
        _inner.Stage(AuditEvent.ForUser(
            AuditActionType.PasswordChanged,
            actorUserId: Guid.CreateVersion7(),
            actorRoleCode: null,
            entityType: "User",
            entityId: Guid.CreateVersion7(),
            occurredAtUtc: auditEvent.OccurredAtUtc,
            correlationId: "corr-forced-audit-failure",
            source: AuditSource.Web));
    }
}
