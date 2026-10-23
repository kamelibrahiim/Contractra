using Contractra.Application.Identity.Users;
using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Identity;

/// <summary>
/// Read-only user listing for the Admin endpoints (auth contract v1.5 §9.2, §9.3).
/// Case-insensitive search and ordering use <c>lower(login_identifier)</c>, the same
/// expression as ux_users_login_identifier_lower (a value-converted LoginIdentifier cannot
/// be lower-cased in LINQ). Roles are those active at <c>asOf</c> (start inclusive, end
/// exclusive, like <c>UserRole.IsActiveAt</c>). The password hash never leaves this class:
/// only whether it is the "no usable password" marker.
/// </summary>
public sealed class UserDirectory : IUserDirectory
{
    private readonly ContractraDbContext _dbContext;

    public UserDirectory(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserDirectoryPage> ListAsync(UserListCriteria criteria, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // Parameters are always non-null strings (an empty one means "no filter") so
        // Npgsql can infer their types; strpos avoids LIKE wildcard escaping.
        var search = criteria.Search ?? string.Empty;
        var status = criteria.Status?.ToString() ?? string.Empty;
        var offset = (long)(criteria.Page - 1) * criteria.PageSize;

        var totalCount = await _dbContext.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM users
                WHERE ({search} = '' OR strpos(lower(login_identifier), lower({search})) > 0)
                  AND ({status} = '' OR status = {status})
                """)
            .SingleAsync(cancellationToken);

        var ids = await _dbContext.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM users
                WHERE ({search} = '' OR strpos(lower(login_identifier), lower({search})) > 0)
                  AND ({status} = '' OR status = {status})
                ORDER BY lower(login_identifier), id
                OFFSET {offset} LIMIT {criteria.PageSize}
                """)
            .ToListAsync(cancellationToken);

        var entries = await LoadAsync(ids, asOf, cancellationToken);
        return new UserDirectoryPage(ids.Select(id => entries[id]).ToList(), totalCount);
    }

    public async Task<UserDirectoryEntry?> FindAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var entries = await LoadAsync([userId], asOf, cancellationToken);
        return entries.GetValueOrDefault(userId);
    }

    private async Task<Dictionary<Guid, UserDirectoryEntry>> LoadAsync(
        IReadOnlyCollection<Guid> ids, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var users = await _dbContext.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.LoginIdentifier,
                u.Status,
                u.IsBootstrapAdmin,
                u.MustChangePassword,
                u.TemporaryPasswordExpiresAt,
                HasUsablePassword = u.PasswordHash != User.UnusablePasswordHash,
            })
            .ToListAsync(cancellationToken);

        var roles = await (
            from userRole in _dbContext.UserRoles.AsNoTracking()
            join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where ids.Contains(userRole.UserId)
                && userRole.AssignedAt <= asOf
                && (userRole.EndedAt == null || userRole.EndedAt > asOf)
            select new { userRole.UserId, role.Code }).ToListAsync(cancellationToken);

        // At most one open ticket per user (ex_password_reset_tickets_one_open_per_user);
        // expired-but-unconsumed tickets are still open, which is what ResetRequired needs.
        var tickets = await _dbContext.PasswordResetTickets.AsNoTracking()
            .Where(t => ids.Contains(t.UserId) && t.ConsumedAt == null && t.SupersededAt == null)
            .Select(t => new { t.UserId, t.ExpiresAt })
            .ToListAsync(cancellationToken);

        // v1.6: the latest credential issuance decides credentialState (contract §23.1a).
        var issuances = await _dbContext.CredentialIssuances.AsNoTracking()
            .Where(i => ids.Contains(i.UserId))
            .Select(i => new { i.UserId, i.Status, i.IssuedAt, i.Id })
            .ToListAsync(cancellationToken);
        var latestIssuanceByUser = issuances
            .GroupBy(i => i.UserId)
            .ToDictionary(g => g.Key, g => (CredentialDeliveryStatus?)g.OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id).First().Status);

        var rolesByUser = roles
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Code.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList());
        var ticketExpiryByUser = tickets.ToDictionary(t => t.UserId, t => (DateTimeOffset?)t.ExpiresAt);

        return users.ToDictionary(
            u => u.Id,
            u => new UserDirectoryEntry(
                u.Id,
                u.LoginIdentifier.Value,
                u.Status,
                u.IsBootstrapAdmin,
                rolesByUser.GetValueOrDefault(u.Id, []),
                u.MustChangePassword,
                u.TemporaryPasswordExpiresAt,
                u.HasUsablePassword,
                ticketExpiryByUser.GetValueOrDefault(u.Id),
                latestIssuanceByUser.GetValueOrDefault(u.Id)));
    }
}
