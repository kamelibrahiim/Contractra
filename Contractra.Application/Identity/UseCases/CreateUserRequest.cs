using Contractra.Domain.Audit;

namespace Contractra.Application.Identity.UseCases;

/// <summary>Auth contract v1.5 §9.1. <paramref name="PartyId"/> is required exactly when <paramref name="RoleCodes"/> contains "Engineer".</summary>
public sealed record CreateUserRequest(
    string LoginIdentifier,
    IReadOnlyList<string> RoleCodes,
    Guid? PartyId,
    Guid ActorUserId,
    string CorrelationId,
    AuditSource Source);
