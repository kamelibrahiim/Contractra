namespace Contractra.Application.Identity;

/// <summary>
/// The <c>users.*</c> ProblemDetails codes of auth contract v1.5 §12. The Slice 4 use
/// cases return these strings in <see cref="UseCaseError.Code"/>; the HTTP layer maps
/// them to a status and title.
/// </summary>
public static class UserErrorCodes
{
    public const string NotFound = "users.not_found";
    public const string LoginIdentifierInvalid = "users.login_identifier_invalid";
    public const string LoginIdentifierTaken = "users.login_identifier_taken";
    public const string RoleRequired = "users.role_required";
    public const string RoleUnknown = "users.role_unknown";
    public const string PartyRequired = "users.party_required";
    public const string PartyInvalid = "users.party_invalid";
    public const string PartyAlreadyLinked = "users.party_already_linked";
    public const string AlreadyDisabled = "users.already_disabled";
    public const string AlreadyActive = "users.already_active";
    public const string InitialPasswordPending = "users.initial_password_pending";
    public const string InitialPasswordNotReissuable = "users.initial_password_not_reissuable";
    public const string ResetDeliveryUnavailable = "users.reset_delivery_unavailable";
    public const string BootstrapAccountProtected = "users.bootstrap_account_protected";
}
