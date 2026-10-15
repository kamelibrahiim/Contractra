namespace Contractra.Domain.Identity;

/// <summary>
/// A single-use password-reset code issued for a user, stored only as its
/// SHA-256 hash (auth contract v1.5 §9.6, §10A; owner instruction R44; ADR 0005
/// §3.3). Open = neither consumed nor superseded. Matching a presented code is
/// the Application's job (constant-time comparison, §5.6); a failed match
/// changes nothing here.
/// </summary>
public sealed class PasswordResetTicket
{
    public const int MaxChannelLength = 32;

    // EF Core maps this field (backing-field convention), so the copy the
    // property returns can never change what is persisted.
    private byte[] _codeHash = null!;

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 of the reset code (a copy); the code itself is never stored.</summary>
    public byte[] CodeHash
    {
        get => (byte[])_codeHash.Clone();
        private set => _codeHash = value;
    }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The Admin who issued it; null when issued by the System (operator command, §10A).</summary>
    public Guid? IssuedByUserId { get; private set; }

    /// <summary>The delivery channel's name (§10A); "operator" until a channel exists (§19 OC-2).</summary>
    public string Channel { get; private set; } = null!;

    public DateTimeOffset? ConsumedAt { get; private set; }

    public DateTimeOffset? SupersededAt { get; private set; }

    public bool IsOpen => ConsumedAt is null && SupersededAt is null;

    private PasswordResetTicket()
    {
    }

    /// <param name="codeHash">SHA-256 of the reset code (32 bytes).</param>
    public static PasswordResetTicket Issue(
        Guid userId,
        byte[] codeHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        Guid? issuedByUserId,
        string channel)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A reset ticket belongs to a user.", nameof(userId));
        }

        if (issuedByUserId == Guid.Empty)
        {
            throw new ArgumentException("The issuing user id cannot be empty; use null for the System.", nameof(issuedByUserId));
        }

        var hash = AuthSession.CopySha256(codeHash, nameof(codeHash));
        EnsureUtc(issuedAt, nameof(issuedAt));
        EnsureUtc(expiresAt, nameof(expiresAt));

        if (expiresAt <= issuedAt)
        {
            throw new ArgumentException("expiresAt must be after issuedAt.", nameof(expiresAt));
        }

        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentException("A reset ticket names its delivery channel.", nameof(channel));
        }

        if (channel.Length != channel.Trim().Length || channel.Length > MaxChannelLength)
        {
            throw new ArgumentException(
                $"The channel cannot have leading or trailing whitespace or exceed {MaxChannelLength} characters.", nameof(channel));
        }

        return new PasswordResetTicket
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CodeHash = hash,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            IssuedByUserId = issuedByUserId,
            Channel = channel,
        };
    }

    /// <summary>Open, and asOf within [IssuedAt, ExpiresAt).</summary>
    public bool IsValidAt(DateTimeOffset asOf) => IsOpen && asOf >= IssuedAt && asOf < ExpiresAt;

    /// <summary>
    /// Closes an open ticket because a newer one is issued (§9.6), expired or not.
    /// </summary>
    public void Supersede(DateTimeOffset supersededAt)
    {
        EnsureUtc(supersededAt, nameof(supersededAt));
        EnsureNotBeforeIssue(supersededAt, nameof(supersededAt));

        if (!IsOpen)
        {
            throw new InvalidOperationException("Only an open reset ticket can be superseded.");
        }

        SupersededAt = supersededAt;
    }

    /// <summary>
    /// Consumes the ticket on a successful reset completion (§5.6): only while
    /// open and before it expires.
    /// </summary>
    public void Consume(DateTimeOffset consumedAt)
    {
        EnsureUtc(consumedAt, nameof(consumedAt));
        EnsureNotBeforeIssue(consumedAt, nameof(consumedAt));

        if (!IsOpen)
        {
            throw new InvalidOperationException("Only an open reset ticket can be consumed.");
        }

        if (consumedAt >= ExpiresAt)
        {
            throw new InvalidOperationException("The reset ticket has expired.");
        }

        ConsumedAt = consumedAt;
    }

    private void EnsureNotBeforeIssue(DateTimeOffset value, string parameterName)
    {
        if (value < IssuedAt)
        {
            throw new ArgumentException("The time must not be before IssuedAt.", parameterName);
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
