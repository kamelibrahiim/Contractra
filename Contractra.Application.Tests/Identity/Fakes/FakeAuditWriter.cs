using Contractra.Application.Audit;
using Contractra.Domain.Audit;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakeAuditWriter : IAuditWriter
{
    public List<AuditEvent> Staged { get; } = new();

    public void Stage(AuditEvent auditEvent) => Staged.Add(auditEvent);
}
