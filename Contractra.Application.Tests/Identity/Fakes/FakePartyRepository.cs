using Contractra.Application.Parties;
using Contractra.Domain.Parties;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakePartyRepository : IPartyRepository
{
    private readonly Dictionary<Guid, Party> _parties = new();
    private readonly HashSet<Guid> _linked = new();

    public void Seed(Party party) => _parties[party.Id] = party;

    public void MarkLinked(Guid partyId) => _linked.Add(partyId);

    public Task<Party?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_parties.GetValueOrDefault(id));

    public Task<bool> IsLinkedToAnyUserAsync(Guid partyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_linked.Contains(partyId));
}
