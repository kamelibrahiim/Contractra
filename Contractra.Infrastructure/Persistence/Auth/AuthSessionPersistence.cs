using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Auth;

/// <summary>
/// Session queries and the set-based revocation (auth contract v1.5 §3.3;
/// ADR 0005 §4). Rotation itself is <see cref="AuthSession.Rotate"/> followed
/// by one SaveChanges: the token's consumed_at and the session's xmin are
/// concurrency tokens, so of two rotations of the same token only one commits.
/// </summary>
public static class AuthSessionPersistence
{
    /// <summary>
    /// The session owning the token with this hash, consumed or not, loading
    /// only that token and the session's unconsumed one - not the whole token
    /// history, which grows by one row per refresh. Null when no token has the
    /// hash. The result is tracked, ready for Rotate or Revoke.
    /// </summary>
    public static async Task<AuthSession?> FindByRefreshTokenHashAsync(
        this DbSet<AuthSession> sessions,
        byte[] tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var hash = CopySha256(tokenHash, nameof(tokenHash));

        return await sessions
            .Include(s => s.RefreshTokens.Where(t => t.TokenHash == hash || t.ConsumedAt == null))
            .SingleOrDefaultAsync(s => s.RefreshTokens.Any(t => t.TokenHash == hash), cancellationToken);
    }

    /// <summary>
    /// Revokes every active session of the user, except
    /// <paramref name="exceptSessionId"/> when given (change-password keeps the
    /// current session, §3.3). Already revoked sessions keep their first
    /// revocation. Runs immediately as one UPDATE, in the caller's transaction
    /// when there is one; sessions already tracked by the context are not
    /// refreshed. Changing the rows changes their xmin, so a rotation that read
    /// one of them earlier fails. Returns the number of sessions revoked.
    /// </summary>
    public static async Task<int> RevokeAllActiveAsync(
        this DbSet<AuthSession> sessions,
        Guid userId,
        SessionRevokeReason reason,
        DateTimeOffset revokedAt,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown session revoke reason.");
        }

        if (revokedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC (offset 00:00).", nameof(revokedAt));
        }

        var active = sessions.Where(s => s.UserId == userId && s.RevokedAt == null);
        if (exceptSessionId is { } except)
        {
            active = active.Where(s => s.Id != except);
        }

        return await active.ExecuteUpdateAsync(
            set => set
                .SetProperty(s => s.RevokedAt, (DateTimeOffset?)revokedAt)
                .SetProperty(s => s.RevokeReason, (SessionRevokeReason?)reason),
            cancellationToken);
    }

    private static byte[] CopySha256(byte[]? hash, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(hash, parameterName);

        if (hash.Length != 32)
        {
            throw new ArgumentException("A SHA-256 hash is exactly 32 bytes.", parameterName);
        }

        return (byte[])hash.Clone();
    }
}
