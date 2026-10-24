using Contractra.Domain.Audit;

namespace Contractra.Application.Identity.UseCases;

public sealed record IssuePasswordResetRequest(Guid TargetUserId, Guid ActorUserId, string CorrelationId, AuditSource Source);
