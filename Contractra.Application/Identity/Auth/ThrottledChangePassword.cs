using Contractra.Application.Common;
using Contractra.Application.Identity.UseCases;
using Contractra.Domain.Audit;

namespace Contractra.Application.Identity.Auth;

/// <param name="FieldErrors">Shape errors the HTTP layer already found (e.g. a non-string value).</param>
public sealed record ChangePasswordAttempt(
    Guid UserId,
    Guid SessionId,
    string? CurrentPassword,
    string? NewPassword,
    string CorrelationId,
    AuditSource Source,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

/// <summary>
/// <c>POST /api/v1/auth/change-password</c> before token rotation (contract §5.5, §17
/// layer 4): admission per user, input shape and limits, then the existing Slice 4
/// <see cref="ChangePasswordUseCase"/> (unchanged; it commits on its own). Only a wrong
/// current password stays counted; any other outcome releases the attempt. Throttling
/// never revokes the session. The HTTP layer rotates the current session's refresh
/// token afterwards, in a separate scope (see ChangePasswordUseCase's transaction note).
/// </summary>
public sealed class ThrottledChangePassword
{
    private readonly ChangePasswordUseCase _useCase;
    private readonly ISessionStore _sessions;
    private readonly IAuthThrottle _throttle;
    private readonly IClock _clock;

    public ThrottledChangePassword(ChangePasswordUseCase useCase, ISessionStore sessions, IAuthThrottle throttle, IClock clock)
    {
        _useCase = useCase;
        _sessions = sessions;
        _throttle = throttle;
        _clock = clock;
    }

    public async Task<AuthOutcome<bool>> ExecuteAsync(ChangePasswordAttempt attempt, CancellationToken cancellationToken = default)
    {
        var admission = await _throttle.AdmitChangePasswordAsync(attempt.UserId, _clock.UtcNow, cancellationToken);
        switch (admission.Status)
        {
            case ThrottleStatus.Throttled:
                return AuthOutcome<bool>.Fail(AuthFailure.Throttled(admission.RetryAfter!.Value));
            case ThrottleStatus.Unavailable:
                return AuthOutcome<bool>.Fail(AuthFailure.ThrottleUnavailable());
        }

        var ticket = admission.Ticket!;
        var outcome = await ChangeAsync(attempt, cancellationToken);
        if (outcome.Failure?.Code != AuthErrorCodes.CurrentPasswordIncorrect)
        {
            await _throttle.RecordChangePasswordNotWrongAsync(ticket, cancellationToken);
        }

        return outcome;
    }

    private async Task<AuthOutcome<bool>> ChangeAsync(ChangePasswordAttempt attempt, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(attempt.FieldErrors ?? new Dictionary<string, string[]>());
        if (string.IsNullOrEmpty(attempt.CurrentPassword) && !errors.ContainsKey("currentPassword"))
        {
            errors["currentPassword"] = ["The current password is required."];
        }
        else if (attempt.CurrentPassword is { Length: > LoginUseCase.MaxPasswordLength })
        {
            errors["currentPassword"] = ["The current password is too long."];
        }

        if (attempt.NewPassword is null && !errors.ContainsKey("newPassword"))
        {
            errors["newPassword"] = ["The new password is required."];
        }

        if (errors.Count > 0)
        {
            return AuthOutcome<bool>.Fail(AuthFailure.Validation(errors));
        }

        // The current refresh token of this session is consumed afterwards (E11); a
        // session that is no longer active has none, so the call is not authenticated.
        var session = await _sessions.FindByIdAsync(attempt.SessionId, cancellationToken);
        if (session is null || session.UserId != attempt.UserId || !session.IsActiveAt(_clock.UtcNow))
        {
            return AuthOutcome<bool>.Fail(AuthFailure.Rejected(AuthErrorCodes.Unauthenticated, "The session is no longer active."));
        }

        var result = await _useCase.ExecuteAsync(new ChangePasswordRequest(
            attempt.UserId, attempt.SessionId, attempt.CurrentPassword!, attempt.NewPassword!, attempt.CorrelationId, attempt.Source), cancellationToken);

        return result.IsSuccess
            ? AuthOutcome<bool>.Success(true)
            : AuthOutcome<bool>.Fail(AuthFailure.Rejected(result.Error!.Code, result.Error.Message));
    }
}
