using System.Globalization;
using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.Credentials;

/// <summary>
/// What the Admin may see about an issuance (contract §23.2, §23.4): status, attempts, a masked
/// destination and a failure category. Never a password, hash, full number, or message text.
/// </summary>
public sealed record CredentialDeliveryView(
    Guid IssuanceId,
    CredentialIssuanceKind Kind,
    CredentialDeliveryStatus Status,
    int Attempts,
    DateTimeOffset UpdatedAt,
    string DestinationMasked,
    CredentialFailureCategory? FailureCategory,
    bool Stalled)
{
    public static CredentialDeliveryView From(CredentialIssuance issuance, DateTimeOffset now, TimeSpan stalledAfter) =>
        new(
            issuance.Id,
            issuance.Kind,
            issuance.Status,
            issuance.Attempts,
            issuance.UpdatedAt,
            WhatsAppNumber.Mask(issuance.Destination),
            issuance.FailureCategory,
            issuance.Status is CredentialDeliveryStatus.Queued or CredentialDeliveryStatus.Sending
                && now - issuance.UpdatedAt > stalledAfter);

    /// <summary>The wire value of a failure category (contract §23.4).</summary>
    public static string? CategoryCode(CredentialFailureCategory? category) => category switch
    {
        null => null,
        CredentialFailureCategory.InvalidNumber => "invalid_number",
        CredentialFailureCategory.NotOnWhatsApp => "not_on_whatsapp",
        CredentialFailureCategory.TemplateUnavailable => "template_unavailable",
        CredentialFailureCategory.AuthOrPermission => "auth_or_permission",
        CredentialFailureCategory.RateLimited => "rate_limited",
        CredentialFailureCategory.ProviderOutage => "provider_outage",
        CredentialFailureCategory.ConsentWithdrawn => "consent_withdrawn",
        CredentialFailureCategory.KeyUnavailable => "key_unavailable",
        _ => "expired_before_delivery",
    };

    public string UpdatedAtUtc => UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
