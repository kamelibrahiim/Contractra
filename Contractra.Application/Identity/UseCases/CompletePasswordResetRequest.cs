using Contractra.Domain.Audit;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.5 §5.6. No authentication; the actor is the user
/// completing their own reset. <paramref name="Source"/> is resolved by the
/// caller (Slice 6) from which app's endpoint received the request.
/// </summary>
public sealed record CompletePasswordResetRequest(
    string LoginIdentifier,
    string ResetCode,
    string NewPassword,
    string CorrelationId,
    AuditSource Source);
