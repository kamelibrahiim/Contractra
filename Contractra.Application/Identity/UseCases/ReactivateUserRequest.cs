using Contractra.Domain.Audit;

namespace Contractra.Application.Identity.UseCases;

public sealed record ReactivateUserRequest(Guid TargetUserId, Guid ActorUserId, string CorrelationId, AuditSource Source);
