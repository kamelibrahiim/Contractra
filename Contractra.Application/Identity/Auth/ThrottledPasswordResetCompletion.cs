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
/// (layers 2 and 3, shared with login) → shape and input limits (§1) → the existing
/// Slice 4 <see cref="CompletePasswordResetUseCase"/>, which applies the §5.6 business
/// rules (user/ticket/code, then the new-password minimum) in its own order. A
/// success clears the pair counter and releases the identifier count; every failure
/// stays counted.
///
/// The §1 transport limit on <c>newPassword</c> (at most 128 characters, checked
/// before any lookup or hashing) is enforced here, alongside the existing
/// <c>loginIdentifier</c>/<c>resetCode</c> limits - not inside the use case, where
/// the identical-looking 8-128 character rule is a business rule checked last,
/// after the user/ticket/code match, and answered with its own
/// <c>auth.password_too_short</c>/<c>auth.password_too_long</c> codes rather than
/// <c>validation.failed</c>. The two checks share a number (128) but are not the
/// same check: an over-128 <c>newPassword</c> never reaches a lookup or a hash.
/// </summary>
public sealed class ThrottledPasswordResetCompletion
{
    public const int MaxResetCodeLength = 32;

    /// <summary>Contract §1's transport limit on any password field, checked before any lookup or hashing.</summary>
    public const int MaxNewPasswordLength = 128;

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
        if (!errors.ContainsKey("newPassword"))
        {
            if (attempt.NewPassword is null)
            {
                errors["newPassword"] = ["The new password is required."];
            }
            else if (attempt.NewPassword.Length > MaxNewPasswordLength)
            {
                // §1's transport limit, not §5.6's minimum-length business rule: this
                // never reaches the use case, so it never reaches a lookup or a hash.
                errors["newPassword"] = ["The new password is too long."];
            }
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
