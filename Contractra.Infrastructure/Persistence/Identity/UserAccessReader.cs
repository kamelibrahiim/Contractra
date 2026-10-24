using Contractra.Application.Identity.Auth;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Identity;

/// <summary>
/// Effective permissions and active roles (auth contract v1.5 §2): permissions of
/// every role assignment active at the time ∪ every direct grant active at the time,
/// with the same windows as <c>UserRole.IsActiveAt</c> / <c>UserPermission.IsActiveAt</c>
/// (start inclusive, end exclusive). Read-only, untracked.
/// </summary>
public sealed class UserAccessReader : IUserAccessReader, ISecurityStampReader
{
    private readonly ContractraDbContext _dbContext;

    public UserAccessReader(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserAccess> GetAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == userId
                && userRole.AssignedAt <= asOf
                && (userRole.EndedAt == null || userRole.EndedAt > asOf)
            select new { role.Id, role.Code }).ToListAsync(cancellationToken);

        var roleIds = roles.Select(r => r.Id).Distinct().ToList();
        var rolePermissions = await (
            from rolePermission in _dbContext.RolePermissions.AsNoTracking()
            join permission in _dbContext.Permissions.AsNoTracking() on rolePermission.PermissionId equals permission.Id
            where roleIds.Contains(rolePermission.RoleId)
            select new { rolePermission.RoleId, permission.Code }).ToListAsync(cancellationToken);

        var directPermissions = await (
            from userPermission in _dbContext.UserPermissions.AsNoTracking()
            join permission in _dbContext.Permissions.AsNoTracking() on userPermission.PermissionId equals permission.Id
            where userPermission.UserId == userId
                && userPermission.GrantedAt <= asOf
                && (userPermission.RevokedAt == null || userPermission.RevokedAt > asOf)
            select permission.Code).ToListAsync(cancellationToken);

        var roleCodeById = roles.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Code.Value);
        var byRole = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (roleId, code) in roleCodeById)
        {
            byRole[code] = rolePermissions
                .Where(rp => rp.RoleId == roleId)
                .Select(rp => rp.Code.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        var effective = byRole.Values.SelectMany(p => p)
            .Concat(directPermissions.Select(p => p.Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new UserAccess(byRole.Keys.Order(StringComparer.Ordinal).ToList(), effective, byRole);
    }

    /// <summary>
    /// The per-request lookup of contract §3.5: one query on the users primary key,
    /// reading security_version and must_change_password.
    /// </summary>
    async Task<SecurityStamp?> ISecurityStampReader.GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SecurityVersion, u.MustChangePassword })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new SecurityStamp(row.SecurityVersion, row.MustChangePassword);
    }
}
