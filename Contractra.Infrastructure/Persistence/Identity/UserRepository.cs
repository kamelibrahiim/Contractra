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

    public Task<bool> AnyActiveUserWithRoleAsync(string roleCode, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        // Same active window as UserRole.IsActiveAt: AssignedAt inclusive, EndedAt exclusive.
        var code = new RoleCode(roleCode);
        return (
            from userRole in _dbContext.UserRoles
            join role in _dbContext.Roles on userRole.RoleId equals role.Id
            join user in _dbContext.Users on userRole.UserId equals user.Id
            where role.Code == code
                && user.Status == UserStatus.Active
                && userRole.AssignedAt <= asOf
                && (userRole.EndedAt == null || userRole.EndedAt > asOf)
            select userRole.Id).AnyAsync(cancellationToken);
    }

    public Task<User?> FindBootstrapAdminAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Users.SingleOrDefaultAsync(u => u.IsBootstrapAdmin, cancellationToken);
}
