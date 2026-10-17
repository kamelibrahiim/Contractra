namespace Contractra.Application.Identity.Auth;

/// <summary>
/// The auth contract v1.5 §12 error codes used by the Slice 6 auth endpoints.
/// Clients branch on these codes, never on titles or details.
/// </summary>
public static class AuthErrorCodes
{
    public const string ValidationFailed = "validation.failed";
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string Unauthenticated = "auth.unauthenticated";
    public const string RefreshInvalid = "auth.refresh_invalid";
    public const string SessionExpired = "auth.session_expired";
    public const string Forbidden = "auth.forbidden";
    public const string PasswordChangeRequired = "auth.password_change_required";
    public const string AppAccessDenied = "auth.app_access_denied";
    public const string CurrentPasswordIncorrect = "auth.current_password_incorrect";
    public const string PasswordTooShort = "auth.password_too_short";
    public const string PasswordTooLong = "auth.password_too_long";
    public const string PasswordSameAsCurrent = "auth.password_same_as_current";
    public const string ResetCodeInvalid = "auth.reset_code_invalid";
    public const string TooManyAttempts = "auth.too_many_attempts";
    public const string ServerError = "server.error";
}
