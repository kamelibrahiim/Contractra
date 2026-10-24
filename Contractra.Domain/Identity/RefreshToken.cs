namespace Contractra.Domain.Identity;

/// <summary>
/// One refresh token of a session, stored only as its SHA-256 hash (auth
/// contract v1.5 §3.1). Owned by <see cref="AuthSession"/>: created and
/// consumed only through the session, so a token is never consumed without its
/// replacement being issued in the same change.
/// </summary>
public sealed class RefreshToken
{
    // EF Core maps this field (backing-field convention), so the copy the
    // property returns can never change what is persisted.
    private byte[] _tokenHash = null!;

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    /// <summary>SHA-256 of the token (a copy); the token itself is never stored.</summary>
    public byte[] TokenHash
    {
        get => (byte[])_tokenHash.Clone();
        private set => _tokenHash = value;
    }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    private RefreshToken()
    {
    }

    /// <param name="tokenHash">Already validated and copied by the session.</param>
    internal RefreshToken(Guid sessionId, byte[] tokenHash, DateTimeOffset issuedAt)
    {
        Id = Guid.CreateVersion7();
        SessionId = sessionId;
        _tokenHash = tokenHash;
        IssuedAt = issuedAt;
    }

    internal bool HasHash(ReadOnlySpan<byte> tokenHash) => tokenHash.SequenceEqual(_tokenHash);

    internal void Consume(DateTimeOffset consumedAt)
    {
        if (ConsumedAt is not null)
        {
            throw new InvalidOperationException($"This refresh token was already consumed at {ConsumedAt:O}.");
        }

        if (consumedAt < IssuedAt)
        {
            throw new ArgumentException("consumedAt must not be before IssuedAt.", nameof(consumedAt));
        }

        ConsumedAt = consumedAt;
    }
}
