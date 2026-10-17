using Contractra.Application.Identity.Auth;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Identity;

/// <summary>
/// Transparent rehash (auth contract v1.5 §5.1, R33): one conditional UPDATE of
/// password_hash only, in the caller's open transaction, that changes nothing when
/// the stored hash is no longer the verified one. The security version is not
/// touched: the password itself did not change.
/// </summary>
public sealed class PasswordRehashStore : IPasswordRehashStore
{
    private readonly ContractraDbContext _dbContext;

    public PasswordRehashStore(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ReplaceHashAsync(Guid userId, string verifiedHash, string newHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifiedHash);
        ArgumentException.ThrowIfNullOrEmpty(newHash);

        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The rehash runs inside the login's transaction; open one first.");
        }

        var updated = await _dbContext.Users
            .Where(u => u.Id == userId && u.PasswordHash == verifiedHash)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.PasswordHash, newHash), cancellationToken);
        return updated == 1;
    }
}
