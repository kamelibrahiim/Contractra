using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Application.Identity.Operations;

/// <param name="Identifier">The submitted login identifier whose layer 3 (contract §17) throttle state is cleared.</param>
/// <param name="Reason">The operator's <c>--reason</c>: 1-1000 characters, no leading or trailing whitespace. Must not contain secrets.</param>
/// <param name="CorrelationId">One per command run (contract §13); a UUID, also the audit event's target (<c>OperatorCommandRun</c>).</param>
public sealed record ClearThrottleRequest(string Identifier, string Reason, string CorrelationId);

/// <summary>
/// Operator command <c>clear-throttle --identifier &lt;text&gt; --reason &lt;text&gt;</c>
/// (contract §22 M5, pending review with OC-8): relieves layer 3's identifier-wide
/// slow-mode state for one login identifier, across every source, for incident
/// response to a legitimate user caught behind a distributed attacker's
/// coordinated slow-mode activation. Clears only that one identifier (hashed, as
/// §17 requires: the raw identifier is never stored, logged or audited); it does
/// not touch layer 2's per-source pairs, any other identifier, or any user's
/// layer 4 change-password state. Audits <c>ThrottleCleared</c> (System actor,
/// source SystemJob, the operator's reason, key
/// <c>ThrottleCleared:command:&lt;run correlation id&gt;</c>, target
/// <c>OperatorCommandRun</c> = the run's UUID). Neither the identifier nor its key
/// hash is returned, logged or audited; logs carry only the correlation id.
/// The throttle-row delete and the audit write run in one unit of work
/// (<see cref="IThrottleIdentifierEraser"/>, ADR 0006 §6): both commit or both
/// roll back. A failure before or during the delete reports
/// <see cref="ThrottleStoreUnavailableCode"/> with nothing changed; a failure
/// saving or committing - including the audit write - rolls the delete back
/// too, so it is reported the same way, never as a partial success.
/// </summary>
public sealed class ClearThrottleUseCase
{
    /// <summary>The throttle store could not be reached, or the transaction could not be committed; nothing changed.</summary>
    public const string ThrottleStoreUnavailableCode = "server.error";

    private readonly IThrottleIdentifierEraser _eraser;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ClearThrottleUseCase> _logger;

    public ClearThrottleUseCase(
        IThrottleIdentifierEraser eraser, IAuditWriter auditWriter, IClock clock, IUnitOfWork unitOfWork,
        ILogger<ClearThrottleUseCase>? logger = null)
    {
        _eraser = eraser;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<ClearThrottleUseCase>.Instance;
    }

    public async Task<UseCaseResult> ExecuteAsync(
        ClearThrottleRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatorReason.IsValid(request.Reason))
        {
            return Fail("validation.failed", "--reason is required: 1 to 1000 characters with no leading or trailing whitespace.");
        }

        if (string.IsNullOrWhiteSpace(request.Identifier))
        {
            return Fail("validation.failed", "--identifier is required.");
        }

        // entity_id is NOT NULL (contract §14.1), and the approved data-minimization
        // decision forbids deriving it from the identifier or its hash. The event
        // therefore targets the operation itself: the per-run UUID the operator
        // command runner generates once per run (the run's correlation id).
        if (!Guid.TryParse(request.CorrelationId, out var commandRunId) || commandRunId == Guid.Empty)
        {
            return Fail("validation.failed", "The command run's correlation id must be a non-empty UUID.");
        }

        var identifierKey = ThrottleKeys.IdentifierKey(request.Identifier);
        var now = _clock.UtcNow;

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            await _eraser.ClearIdentifierAsync(identifierKey, cancellationToken);

            _auditWriter.Stage(AuditEvent.ForSystem(
                AuditActionType.ThrottleCleared,
                entityType: "OperatorCommandRun",
                entityId: commandRunId,
                occurredAtUtc: now,
                correlationId: request.CorrelationId,
                idempotencyOperationKey: $"command:{request.CorrelationId}",
                reason: request.Reason));

            await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Both writes share one transaction (IThrottleIdentifierEraser,
            // ADR 0006 §6): a failure here, whether from the delete, the audit
            // insert, the save or the commit, rolls back everything the unit of
            // work staged, so the throttle row is exactly as it was before this
            // call - never a partial success.
            _logger.LogWarning(
                exception,
                "Auth.ThrottleClearFailed CorrelationId={CorrelationId}: nothing was changed; the attempt rolled back.",
                request.CorrelationId);
            return Fail(
                ThrottleStoreUnavailableCode,
                $"The throttle could not be cleared ({exception.GetType().Name}); nothing was changed.");
        }

        _logger.LogInformation("Auth.ThrottleCleared CorrelationId={CorrelationId}", request.CorrelationId);
        return UseCaseResult.Success();
    }

    private static UseCaseResult Fail(string code, string message) => UseCaseResult.Failure(code, message);
}
