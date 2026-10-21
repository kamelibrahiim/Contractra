using Contractra.Application.Identity;
using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Identity;

public sealed class RoleRepository : IRoleRepository
{
    private readonly ContractraDbContext _dbContext;

    public RoleRepository(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Case-insensitive, using the existing ux_roles_code_lower expression
    /// index (ADR 0002 §3) the same way UserLoginLookup uses
    /// ux_users_login_identifier_lower - a value-converted RoleCode cannot be
    /// compared with ToLower() in LINQ (it does not translate).
    /// </summary>
    public Task<Role?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        _dbContext.Roles
            .FromSqlInterpolated($"SELECT * FROM roles WHERE lower(code) = lower({code})")
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListActiveCodesAsync(CancellationToken cancellationToken = default)
    {
        var codes = await _dbContext.Roles.AsNoTracking()
            .Where(r => r.Status == RoleStatus.Active)
            .Select(r => r.Code)
            .ToListAsync(cancellationToken);
        return codes.Select(c => c.Value).ToList();
    }
}
