using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Infrastructure.Persistence.Audit;

/// <summary>
/// AccessDenied (contract v1.5 §6, §14, §14.2; ADR 0006 §6): written in its own DI
/// scope - a fresh DbContext and unit of work - in one short transaction, so it can
/// never save unrelated pending changes of the request. If the write fails the
/// request is still refused with its 403, and Serilog records <c>Audit.WriteFailed</c>
/// with the correlation id and the denied action.
///
/// Target (decided in Slice 6, ADR 0006 E36, §11): <c>entity_type = User</c>,
/// <c>entity_id</c> = the actor's own user id - the account whose access was refused.
/// actor_role_code follows contract §14.1 (<see cref="ActorRoleCodeRule"/>; OC-6).
/// </summary>
public sealed class ScopedAccessDeniedAuditor : IAccessDeniedAuditor
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;
    private readonly ILogger<ScopedAccessDeniedAuditor> _logger;

    public ScopedAccessDeniedAuditor(IServiceScopeFactory scopes, IClock clock, ILogger<ScopedAccessDeniedAuditor>? logger = null)
    {
        _scopes = scopes;
        _clock = clock;
        _logger = logger ?? NullLogger<ScopedAccessDeniedAuditor>.Instance;
    }

    public async Task RecordAsync(AccessDeniedRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            // Never cancelled with the request: a client disconnect must not skip the record.
            await using var scope = _scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var now = _clock.UtcNow;

            var access = await services.GetRequiredService<IUserAccessReader>().GetAsync(record.ActorUserId, now, CancellationToken.None);
            var actorRoleCode = ActorRoleCodeRule.Resolve(access, record.RequiredPermission);

            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            await unitOfWork.BeginTransactionAsync(CancellationToken.None);
            services.GetRequiredService<IAuditWriter>().Stage(AuditEvent.ForUser(
                AuditActionType.AccessDenied,
                actorUserId: record.ActorUserId,
                actorRoleCode: actorRoleCode,
                entityType: "User",
                entityId: record.ActorUserId,
                occurredAtUtc: now,
                correlationId: record.CorrelationId,
                source: record.Source,
                reason: $"{record.DeniedCode}: {record.Action}"));
            await unitOfWork.SaveChangesAndCommitAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Audit.WriteFailed ActionType={ActionType} CorrelationId={CorrelationId} ActorUserId={ActorUserId} DeniedCode={DeniedCode} DeniedAction={DeniedAction}: the request stays refused.",
                nameof(AuditActionType.AccessDenied), record.CorrelationId, record.ActorUserId, record.DeniedCode, record.Action);
        }
    }
}
