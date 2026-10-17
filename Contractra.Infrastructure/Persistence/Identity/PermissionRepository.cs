using Contractra.Application.Identity;
using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Identity;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly ContractraDbContext _dbContext;

    public PermissionRepository(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Permission?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var permissionCode = new PermissionCode(code);
        return _dbContext.Permissions.SingleOrDefaultAsync(p => p.Code == permissionCode, cancellationToken);
    }
}
