namespace Contractra.Domain.Identity;

/// <summary>
/// One login on one client app: a family of refresh tokens (auth contract v1.5
/// §3.3; ADR 0005). Rotation consumes the presented token and issues its
/// replacement in the same change; a revoked or expired session cannot rotate.
/// The idle timeout and the reuse grace depend on roles and configuration and
/// are decided by the Application layer (Slice 4), not here.
/// Only the tokens loaded on this instance are checked; the database is the
/// final guard (one unconsumed token per session, checked at commit).
/// </summary>
public sealed class AuthSession
{
    private readonly List<RefreshToken> _refreshTokens = new();

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public ClientApp ClientApp { get; private set; }

    public ClientPlatform ClientPlatform { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Created + 7 days (owner decisions R5, R17), computed by the caller.
    /// Expiry is derived from this value; it is never written as a revocation.
    /// </summary>
    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    /// <summary>Set at login and at every rotation (auth contract §3.6).</summary>
    public DateTimeOffset LastRefreshedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public SessionRevokeReason? RevokeReason { get; private set; }

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private AuthSession()
    {
    }

    /// <param name="firstTokenHash">SHA-256 of the first refresh token (32 bytes).</param>
    public static AuthSession Start(
        Guid userId,
        ClientApp clientApp,
        ClientPlatform clientPlatform,
        byte[] firstTokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset absoluteExpiresAt)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A session belongs to a user.", nameof(userId));
        }

        EnsureDefined(clientApp, nameof(clientApp));
        EnsureDefined(clientPlatform, nameof(clientPlatform));
        var hash = CopySha256(firstTokenHash, nameof(firstTokenHash));
        EnsureUtc(createdAt, nameof(createdAt));
        EnsureUtc(absoluteExpiresAt, nameof(absoluteExpiresAt));

        if (absoluteExpiresAt <= createdAt)
        {
            throw new ArgumentException("absoluteExpiresAt must be after createdAt.", nameof(absoluteExpiresAt));
        }

        var session = new AuthSession
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ClientApp = clientApp,
            ClientPlatform = clientPlatform,
            CreatedAt = createdAt,
            AbsoluteExpiresAt = absoluteExpiresAt,
            LastRefreshedAt = createdAt,
        };
        session._refreshTokens.Add(new RefreshToken(session.Id, hash, createdAt));
        return session;
    }

    /// <summary>
    /// Not revoked, and asOf within [CreatedAt, AbsoluteExpiresAt).
    /// </summary>
    public bool IsActiveAt(DateTimeOffset asOf) =>
        RevokedAt is null && asOf >= CreatedAt && asOf < AbsoluteExpiresAt;

    /// <summary>
    /// Consumes the presented unconsumed token and issues its replacement
    /// (auth contract §3.3; also used by change-password, plan E11). A rejected
    /// call changes nothing.
    /// </summary>
    public RefreshToken Rotate(byte[] presentedTokenHash, byte[] newTokenHash, DateTimeOffset rotatedAt)
    {
        var presented = CopySha256(presentedTokenHash, nameof(presentedTokenHash));
        var replacement = CopySha256(newTokenHash, nameof(newTokenHash));
        EnsureUtc(rotatedAt, nameof(rotatedAt));

        if (rotatedAt < LastRefreshedAt)
        {
            throw new ArgumentException("rotatedAt must not be before LastRefreshedAt.", nameof(rotatedAt));
        }

        if (_refreshTokens.Any(t => t.HasHash(replacement)))
        {
            throw new ArgumentException("The replacement token must differ from every token of this session.", nameof(newTokenHash));
        }

        if (!IsActiveAt(rotatedAt))
        {
            throw new InvalidOperationException("Only an active session can rotate its refresh token.");
        }

        var current = _refreshTokens.SingleOrDefault(t => t.HasHash(presented))
            ?? throw new InvalidOperationException("The presented refresh token is not a token of this session.");

        if (current.ConsumedAt is not null)
        {
            throw new InvalidOperationException("The presented refresh token was already consumed.");
        }

        if (_refreshTokens.Any(t => t.ConsumedAt is null && !ReferenceEquals(t, current)))
        {
            throw new InvalidOperationException("This session has more than one unconsumed refresh token.");
        }

        current.Consume(rotatedAt);
        var next = new RefreshToken(Id, replacement, rotatedAt);
        _refreshTokens.Add(next);
        LastRefreshedAt = rotatedAt;
        return next;
    }

    /// <summary>
    /// Revokes the session. The first revocation is kept: returns false and
    /// changes nothing when the session is already revoked.
    /// </summary>
    public bool Revoke(SessionRevokeReason reason, DateTimeOffset revokedAt)
    {
        EnsureDefined(reason, nameof(reason));
        EnsureUtc(revokedAt, nameof(revokedAt));

        if (revokedAt < CreatedAt)
        {
            throw new ArgumentException("revokedAt must not be before CreatedAt.", nameof(revokedAt));
        }

        if (RevokedAt is not null)
        {
            return false;
        }

        RevokedAt = revokedAt;
        RevokeReason = reason;
        return true;
    }

    internal static byte[] CopySha256(byte[]? hash, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(hash, parameterName);

        if (hash.Length != 32)
        {
            throw new ArgumentException("A SHA-256 hash is exactly 32 bytes.", parameterName);
        }

        return (byte[])hash.Clone();
    }

    private static void EnsureDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"Unknown {typeof(TEnum).Name}.");
        }
    }

    // Rejected rather than converted, as for UserRole and UserPermission.
    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC (offset 00:00).", parameterName);
        }
    }
}
