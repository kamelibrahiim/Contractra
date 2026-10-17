using Contractra.Application.Common;
using Contractra.Application.Identity.UseCases;
using Contractra.Domain.Audit;

namespace Contractra.Application.Identity.Auth;

public sealed record PasswordResetCompletionAttempt(
    string? LoginIdentifier,
    string? ResetCode,
    string? NewPassword,
    string ClientSource,
    string CorrelationId,
    AuditSource Source,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

/// <summary>
/// <c>POST /api/v1/auth/password-reset/complete</c> (contract §5.6, §17): throttling
/// (layers 2 and 3, shared with login) → shape and input limits → the existing Slice 4
/// <see cref="CompletePasswordResetUseCase"/> (unchanged). A success clears the pair
/// counter and releases the identifier count; every failure stays counted.
/// </summary>
public sealed class ThrottledPasswordResetCompletion
{
    public const int MaxResetCodeLength = 32;

    private readonly CompletePasswordResetUseCase _useCase;
    private readonly IAuthThrottle _throttle;
    private readonly IClock _clock;

    public ThrottledPasswordResetCompletion(CompletePasswordResetUseCase useCase, IAuthThrottle throttle, IClock clock)
    {
        _useCase = useCase;
        _throttle = throttle;
        _clock = clock;
    }

    public async Task<AuthOutcome<bool>> ExecuteAsync(PasswordResetCompletionAttempt attempt, CancellationToken cancellationToken = default)
    {
        ThrottleTicket? ticket = null;
        if (!string.IsNullOrEmpty(attempt.LoginIdentifier))
        {
            var admission = await _throttle.AdmitCredentialAttemptAsync(
                ThrottleKeys.IdentifierKey(attempt.LoginIdentifier), attempt.ClientSource, _clock.UtcNow, cancellationToken);
            switch (admission.Status)
            {
                case ThrottleStatus.Throttled:
                    return AuthOutcome<bool>.Fail(AuthFailure.Throttled(admission.RetryAfter!.Value));
                case ThrottleStatus.Unavailable:
                    return AuthOutcome<bool>.Fail(AuthFailure.ThrottleUnavailable());
            }

            ticket = admission.Ticket;
        }

        var errors = new Dictionary<string, string[]>(attempt.FieldErrors ?? new Dictionary<string, string[]>());
        Require(errors, "loginIdentifier", attempt.LoginIdentifier, LoginUseCase.MaxLoginIdentifierLength);
        Require(errors, "resetCode", attempt.ResetCode, MaxResetCodeLength);
        if (attempt.NewPassword is null && !errors.ContainsKey("newPassword"))
        {
            errors["newPassword"] = ["The new password is required."];
        }

        if (errors.Count > 0)
        {
            return AuthOutcome<bool>.Fail(AuthFailure.Validation(errors));
        }

        var result = await _useCase.ExecuteAsync(new CompletePasswordResetRequest(
            attempt.LoginIdentifier!, attempt.ResetCode!, attempt.NewPassword!, attempt.CorrelationId, attempt.Source), cancellationToken);

        if (!result.IsSuccess)
        {
            return AuthOutcome<bool>.Fail(AuthFailure.Rejected(result.Error!.Code, result.Error.Message));
        }

        if (ticket is not null)
        {
            await _throttle.RecordCredentialSuccessAsync(ticket, cancellationToken);
        }

        return AuthOutcome<bool>.Success(true);
    }

    private static void Require(Dictionary<string, string[]> errors, string field, string? value, int maxLength)
    {
        if (errors.ContainsKey(field))
        {
            return;
        }

        if (string.IsNullOrEmpty(value))
        {
            errors[field] = [$"The {field} is required."];
        }
        else if (value.Length > maxLength)
        {
            errors[field] = [$"The {field} is too long."];
        }
    }
}
