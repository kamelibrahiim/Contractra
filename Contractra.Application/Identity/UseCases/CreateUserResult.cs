using Contractra.Application.Identity.Credentials;

namespace Contractra.Application.Identity.UseCases;

/// <summary>Auth contract v1.6 §23.2 response: the new user and the delivery status. It carries no password.</summary>
public sealed record CreateUserResult(
    Guid UserId,
    string LoginIdentifier,
    IReadOnlyList<string> RoleCodes,
    CredentialDeliveryView Delivery);
