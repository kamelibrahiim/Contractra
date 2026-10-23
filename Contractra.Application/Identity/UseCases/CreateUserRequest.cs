using Contractra.Domain.Audit;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.6 §23.2. <paramref name="PartyId"/> is required exactly when <paramref name="RoleCodes"/> contains "Engineer".
/// <paramref name="WhatsAppNumber"/> is the destination typed by the Admin; <paramref name="ConsentAffirmed"/> and
/// <paramref name="ConsentBasis"/> (<c>written</c> or <c>verbal</c>) record that the employee agreed to receive account messages (OD-21).
/// </summary>
public sealed record CreateUserRequest(
    string LoginIdentifier,
    IReadOnlyList<string> RoleCodes,
    Guid? PartyId,
    string? WhatsAppNumber,
    bool ConsentAffirmed,
    string? ConsentBasis,
    Guid ActorUserId,
    string CorrelationId,
    AuditSource Source);
