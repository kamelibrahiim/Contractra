namespace Contractra.Infrastructure.Persistence.Throttling;

/// <summary>
/// Identifies one throttle row: a persisted scope and the SHA-256 of the
/// throttle key. Compared by value; ordered by scope, then by hash bytes - the
/// order in which <see cref="AuthThrottleStore"/> locks rows, so two steps on
/// overlapping keys cannot deadlock (ADR 0005 §4).
/// </summary>
public sealed class AuthThrottleKey : IEquatable<AuthThrottleKey>, IComparable<AuthThrottleKey>
{
    private readonly byte[] _keyHash;

    public AuthThrottleKey(string scope, byte[] keyHash)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(keyHash);

        if (!AuthThrottleScopes.All.Contains(scope, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown throttle scope '{scope}'.", nameof(scope));
        }

        if (keyHash.Length != 32)
        {
            throw new ArgumentException("A throttle key hash is a SHA-256 hash of exactly 32 bytes.", nameof(keyHash));
        }

        Scope = scope;
        _keyHash = (byte[])keyHash.Clone();
    }

    public string Scope { get; }

    public ReadOnlyMemory<byte> KeyHash => _keyHash;

    internal byte[] KeyHashBytes => _keyHash;

    public bool Equals(AuthThrottleKey? other) =>
        other is not null && string.Equals(Scope, other.Scope, StringComparison.Ordinal) && _keyHash.AsSpan().SequenceEqual(other._keyHash);

    public override bool Equals(object? obj) => obj is AuthThrottleKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Scope, StringComparer.Ordinal);
        hash.AddBytes(_keyHash);
        return hash.ToHashCode();
    }

    public int CompareTo(AuthThrottleKey? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byScope = string.CompareOrdinal(Scope, other.Scope);
        return byScope != 0 ? byScope : _keyHash.AsSpan().SequenceCompareTo(other._keyHash);
    }
}
