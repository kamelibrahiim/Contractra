using System.Security.Cryptography;
using System.Text;
using Contractra.Application.Identity;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Auth;

internal sealed class FakeUserAccessReader : IUserAccessReader, ISecurityStampReader
{
    private readonly Dictionary<Guid, UserAccess> _access = new();

    public void Set(Guid userId, string[] roles, Dictionary<string, string[]>? permissionsByRole = null, string[]? direct = null)
    {
        var byRole = roles.ToDictionary(r => r, r => (IReadOnlyList<string>)(permissionsByRole?.GetValueOrDefault(r) ?? []));
        var effective = byRole.Values.SelectMany(p => p).Concat(direct ?? []).Distinct().Order(StringComparer.Ordinal).ToList();
        _access[userId] = new UserAccess(roles.Order(StringComparer.Ordinal).ToList(), effective, byRole);
    }

    public Task<UserAccess> GetAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
        Task.FromResult(_access.GetValueOrDefault(userId, UserAccess.None));

    Task<SecurityStamp?> ISecurityStampReader.GetAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<SecurityStamp?>(null);
}

/// <summary>Sessions shared with <see cref="Fakes.FakeSessionStore"/> semantics, found by token hash.</summary>
internal sealed class FakeAuthSessionRepository : IAuthSessionRepository
{
    public List<AuthSession> Sessions { get; } = new();

    public void Add(AuthSession session) => Sessions.Add(session);

    public Task<AuthSession?> FindByRefreshTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(Sessions.SingleOrDefault(s => s.RefreshTokens.Any(t => t.TokenHash.AsSpan().SequenceEqual(tokenHash))));
}

internal sealed class FakeRehashStore : IPasswordRehashStore
{
    public List<(Guid UserId, string Old, string New)> Calls { get; } = new();

    public Task<bool> ReplaceHashAsync(Guid userId, string verifiedHash, string newHash, CancellationToken cancellationToken = default)
    {
        Calls.Add((userId, verifiedHash, newHash));
        return Task.FromResult(true);
    }
}

internal sealed class FakeRefreshTokenFactory : IRefreshTokenFactory
{
    private int _next;

    public NewRefreshToken Create()
    {
        var token = $"refresh-token-{Interlocked.Increment(ref _next)}";
        return new NewRefreshToken(token, Hash(token));
    }

    public byte[] Hash(string presentedToken) => SHA256.HashData(Encoding.UTF8.GetBytes(presentedToken));
}

internal sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
{
    public List<AccessTokenSubject> Issued { get; } = new();

    public IssuedAccessToken Issue(AccessTokenSubject subject, DateTimeOffset issuedAt)
    {
        Issued.Add(subject);
        return new IssuedAccessToken($"access-{Issued.Count}", issuedAt.AddMinutes(30));
    }
}

/// <summary>Scripted admissions; records every call.</summary>
internal sealed class FakeAuthThrottle : IAuthThrottle
{
    public ThrottleStatus NextStatus { get; set; } = ThrottleStatus.Admitted;

    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(30);

    public List<string> Calls { get; } = new();

    public Task<ThrottleAdmission> AdmitCredentialAttemptAsync(byte[] identifierKey, string clientSource, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        Calls.Add($"admit-credential:{ThrottleKeys.ToHex(identifierKey)}:{clientSource}");
        return Task.FromResult(Admission(now));
    }

    public Task RecordCredentialSuccessAsync(ThrottleTicket ticket, CancellationToken cancellationToken = default)
    {
        Calls.Add("credential-success");
        return Task.CompletedTask;
    }

    public Task<ThrottleAdmission> AdmitChangePasswordAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        Calls.Add("admit-change-password");
        return Task.FromResult(Admission(now));
    }

    public Task RecordChangePasswordNotWrongAsync(ThrottleTicket ticket, CancellationToken cancellationToken = default)
    {
        Calls.Add("change-password-released");
        return Task.CompletedTask;
    }

    private ThrottleAdmission Admission(DateTimeOffset now) => NextStatus switch
    {
        ThrottleStatus.Admitted => new ThrottleAdmission(ThrottleStatus.Admitted, Ticket: new ThrottleTicket([new byte[32]], now, false)),
        ThrottleStatus.Throttled => new ThrottleAdmission(ThrottleStatus.Throttled, RetryAfter),
        _ => ThrottleAdmission.Unavailable(),
    };
}

/// <summary>
/// Scripted identifier clears for <see cref="ClearThrottleUseCase"/>. Unlike a
/// real <see cref="Contractra.Infrastructure.Persistence.Throttling.ThrottleIdentifierEraser"/>,
/// this fake has no transaction of its own to roll back - it only records
/// whether the use case called it before the surrounding
/// <see cref="FakeUnitOfWork"/> committed, which is what the use case's own
/// atomicity depends on; real rollback of the deleted row is an
/// Infrastructure/Integration-level concern (ADR 0006 §6), not provable here.
/// </summary>
internal sealed class FakeThrottleIdentifierEraser : IThrottleIdentifierEraser
{
    public List<string> ClearedIdentifiers { get; } = new();

    /// <summary>When set, <see cref="ClearIdentifierAsync"/> throws instead of clearing - simulates the store being unreachable.</summary>
    public bool FailOnClear { get; set; }

    /// <summary>Whether to report a row existed (true) or there was nothing to clear (false).</summary>
    public bool RowExisted { get; set; } = true;

    public Task<bool> ClearIdentifierAsync(byte[] identifierKey, CancellationToken cancellationToken = default)
    {
        if (FailOnClear)
        {
            throw new InvalidOperationException("Simulated throttle-store failure.");
        }

        ClearedIdentifiers.Add(ThrottleKeys.ToHex(identifierKey));
        return Task.FromResult(RowExisted);
    }
}

/// <summary>Counts hash verifications, so tests can prove the dummy-hash and no-hash rules.</summary>
internal sealed class CountingPasswordHasher : IPasswordHasher
{
    private readonly Fakes.FakePasswordHasher _inner = new();

    public List<string> VerifiedHashes { get; } = new();

    public PasswordVerificationResult? Override { get; set; }

    public string Hash(string password) => _inner.Hash(password);

    public PasswordVerificationResult Verify(string hash, string password)
    {
        VerifiedHashes.Add(hash);
        var result = _inner.Verify(hash, password);
        return result == PasswordVerificationResult.Success && Override is { } forced ? forced : result;
    }
}
