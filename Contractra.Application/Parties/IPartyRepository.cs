using Contractra.Domain.Parties;

namespace Contractra.Application.Parties;

/// <summary>Read access to Party needed by CreateUser's Engineer-link validation (contract §9.1, E8).</summary>
public interface IPartyRepository
{
    Task<Party?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True when any user is already linked to this party (one account per Party, R30).</summary>
    Task<bool> IsLinkedToAnyUserAsync(Guid partyId, CancellationToken cancellationToken = default);
}
