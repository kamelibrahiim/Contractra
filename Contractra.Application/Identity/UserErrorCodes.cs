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

    // Contract v1.6 §23.7 (credential delivery, R49-R51).
    public const string CredentialDeliveryUnavailable = "users.credential_delivery_unavailable";
    public const string CredentialDeliveryNotFound = "users.credential_delivery_not_found";
    public const string WhatsAppNumberInvalid = "users.whatsapp_number_invalid";
    public const string WhatsAppConsentRequired = "users.whatsapp_consent_required";
    public const string WhatsAppDestinationMissing = "users.whatsapp_destination_missing";
    public const string ReissueTooSoon = "users.reissue_too_soon";
    public const string IssuanceChanged = "users.issuance_changed";
    public const string SelfResetNotAllowed = "users.self_reset_not_allowed";
}
