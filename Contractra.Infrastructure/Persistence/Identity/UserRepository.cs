using Contractra.Application.Identity;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Persistence.Queries;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Identity;

public sealed class UserRepository : IUserRepository
{
    private readonly ContractraDbContext _dbContext;

    public UserRepository(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<User?> FindByLoginIdentifierAsync(string loginIdentifier, CancellationToken cancellationToken = default) =>
        _dbContext.Users.WhereLoginIdentifierMatches(loginIdentifier).SingleOrDefaultAsync(cancellationToken);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<bool> ExistsWithLoginIdentifierAsync(string loginIdentifier, CancellationToken cancellationToken = default) =>
        _dbContext.Users.WhereLoginIdentifierMatches(loginIdentifier).AnyAsync(cancellationToken);

    public void Add(User user) => _dbContext.Users.Add(user);
}
