using Contractra.Domain.Audit;

namespace Contractra.Application.Identity.UseCases;

public sealed record DisableUserRequest(Guid TargetUserId, Guid ActorUserId, string CorrelationId, AuditSource Source);
