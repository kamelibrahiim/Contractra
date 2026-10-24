using Contractra.Domain.Audit;

namespace Contractra.Application.Identity.Auth;

/// <summary>Why an auth use case refused a request; <see cref="Code"/> is a contract §12 code.</summary>
public enum AuthFailureKind
{
    /// <summary>A 4xx outcome with the given code.</summary>
    Rejected,

    /// <summary>Throttled (contract §17): 429 with <see cref="AuthFailure.RetryAfter"/>.</summary>
    Throttled,

    /// <summary>The shared throttle store could not be reached: fail closed (contract §17, 503).</summary>
    ThrottleUnavailable,
}

/// <summary>
/// The user an <c>AccessDenied</c> audit event must name (contract §6, §14): only set
/// when the caller was authenticated - at login, after the password was verified.
/// </summary>
/// <param name="RequiredPermission">The permission the refused action required (for contract §14.1 actor_role_code).</param>
public sealed record DeniedActor(Guid UserId, AuditSource Source, string? RequiredPermission);

public sealed record AuthFailure(
    AuthFailureKind Kind,
    string Code,
    string Message,
    TimeSpan? RetryAfter = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    DeniedActor? DeniedActor = null)
{
    public static AuthFailure Rejected(string code, string message) => new(AuthFailureKind.Rejected, code, message);

    public static AuthFailure Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(AuthFailureKind.Rejected, AuthErrorCodes.ValidationFailed, "The request is invalid.", FieldErrors: errors);

    public static AuthFailure Throttled(TimeSpan retryAfter) =>
        new(AuthFailureKind.Throttled, AuthErrorCodes.TooManyAttempts, "Too many attempts; try again later.", RetryAfter: retryAfter);

    public static AuthFailure ThrottleUnavailable() =>
        new(AuthFailureKind.ThrottleUnavailable, AuthErrorCodes.ServerError, "The service is temporarily unavailable.");
}

/// <summary>An auth use case's outcome: a value, or an <see cref="AuthFailure"/>.</summary>
public sealed class AuthOutcome<T>
{
    private AuthOutcome(T? value, AuthFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public AuthFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static AuthOutcome<T> Success(T value) => new(value, null);

    public static AuthOutcome<T> Fail(AuthFailure failure) => new(default, failure);
}
