using Contractra.Application.Parties;
using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Parties;

public sealed class PartyRepository : IPartyRepository
{
    private readonly ContractraDbContext _dbContext;

    public PartyRepository(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Party?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Parties.Include(p => p.Roles).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> IsLinkedToAnyUserAsync(Guid partyId, CancellationToken cancellationToken = default) =>
        _dbContext.UserPartyLinks.AnyAsync(l => l.PartyId == partyId, cancellationToken);
}
