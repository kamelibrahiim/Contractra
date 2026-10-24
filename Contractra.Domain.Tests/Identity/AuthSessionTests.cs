using System.Security.Cryptography;
using Contractra.Domain.Identity;
using static Contractra.Domain.Tests.Identity.TestUsers;

namespace Contractra.Domain.Tests.Identity;

/// <summary>
/// Sessions and refresh-token rotation (Backend Sprint 2, US-BE-008; auth
/// contract v1.5 §3.3; ADR 0005 §3.1, §3.2, §4).
/// </summary>
public class AuthSessionTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Expires = T0.AddDays(7);

    [Fact]
    public void Start_CreatesAnActiveSession_WithItsFirstUnconsumedToken()
    {
        var hash = NewHash();

        var session = AuthSession.Start(UserId, ClientApp.Employee, ClientPlatform.Mobile, hash, T0, Expires);

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(UserId, session.UserId);
        Assert.Equal(ClientApp.Employee, session.ClientApp);
        Assert.Equal(ClientPlatform.Mobile, session.ClientPlatform);
        Assert.Equal(T0, session.CreatedAt);
        Assert.Equal(Expires, session.AbsoluteExpiresAt);
        Assert.Equal(T0, session.LastRefreshedAt);
        Assert.Null(session.RevokedAt);
        Assert.Null(session.RevokeReason);

        var token = Assert.Single(session.RefreshTokens);
        Assert.Equal(session.Id, token.SessionId);
        Assert.Equal(hash, token.TokenHash);
        Assert.Equal(T0, token.IssuedAt);
        Assert.Null(token.ConsumedAt);
    }

    [Fact]
    public void Start_CopiesTheHash_SoTheCallerCannotChangeItLater()
    {
        var hash = NewHash();
        var session = AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, hash, T0, Expires);

        hash[0] ^= 0xFF;

        Assert.NotEqual(hash, session.RefreshTokens.Single().TokenHash);
    }

    [Fact]
    public void TokenHash_ReturnsACopy_SoChangingItCannotChangeTheStoredHash()
    {
        var hash = NewHash();
        var token = AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, hash, T0, Expires).RefreshTokens.Single();

        token.TokenHash[0] ^= 0xFF;

        Assert.Equal(hash, token.TokenHash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Start_RejectsAHashThatIsNotSha256Sized(int length)
    {
        Assert.Throws<ArgumentException>(() =>
            AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, new byte[length], T0, Expires));
    }

    [Fact]
    public void Start_RejectsMissingValues()
    {
        Assert.Throws<ArgumentNullException>(() => AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, null!, T0, Expires));
        Assert.Throws<ArgumentException>(() => AuthSession.Start(Guid.Empty, ClientApp.Admin, ClientPlatform.Web, NewHash(), T0, Expires));
        Assert.Throws<ArgumentOutOfRangeException>(() => AuthSession.Start(UserId, (ClientApp)99, ClientPlatform.Web, NewHash(), T0, Expires));
        Assert.Throws<ArgumentOutOfRangeException>(() => AuthSession.Start(UserId, ClientApp.Admin, (ClientPlatform)99, NewHash(), T0, Expires));
    }

    [Fact]
    public void Start_RequiresUtc_AndAnExpiryAfterCreation()
    {
        var local = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() => AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, NewHash(), local, Expires));
        Assert.Throws<ArgumentException>(() => AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, NewHash(), T0, local.AddDays(7)));
        Assert.Throws<ArgumentException>(() => AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, NewHash(), T0, T0));
    }

    [Fact]
    public void IsActiveAt_FromCreationUntilTheAbsoluteExpiry_AndNotOnceRevoked()
    {
        var session = NewSession();

        Assert.False(session.IsActiveAt(T0.AddTicks(-1)));
        Assert.True(session.IsActiveAt(T0));
        Assert.True(session.IsActiveAt(Expires.AddTicks(-1)));
        Assert.False(session.IsActiveAt(Expires));

        session.Revoke(SessionRevokeReason.Logout, T0.AddHours(1));

        Assert.False(session.IsActiveAt(T0.AddHours(2)));
    }

    [Fact]
    public void Rotate_ConsumesThePresentedToken_IssuesTheReplacement_AndRecordsTheRefresh()
    {
        var first = NewHash();
        var session = AuthSession.Start(UserId, ClientApp.Employee, ClientPlatform.Mobile, first, T0, Expires);
        var second = NewHash();
        var at = T0.AddMinutes(30);

        var replacement = session.Rotate(first, second, at);

        Assert.Equal(second, replacement.TokenHash);
        Assert.Equal(at, replacement.IssuedAt);
        Assert.Null(replacement.ConsumedAt);
        Assert.Equal(session.Id, replacement.SessionId);
        Assert.Equal(at, session.RefreshTokens.Single(t => t.TokenHash.SequenceEqual(first)).ConsumedAt);
        Assert.Equal(at, session.LastRefreshedAt);
        Assert.Equal(2, session.RefreshTokens.Count);
        Assert.Single(session.RefreshTokens, t => t.ConsumedAt is null);
    }

    [Fact]
    public void Rotate_CanChain_AlwaysLeavingExactlyOneLiveToken()
    {
        var current = NewHash();
        var session = AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, current, T0, Expires);

        for (var i = 1; i <= 3; i++)
        {
            var next = NewHash();
            session.Rotate(current, next, T0.AddMinutes(30 * i));
            current = next;
        }

        Assert.Equal(4, session.RefreshTokens.Count);
        Assert.Equal(current, Assert.Single(session.RefreshTokens, t => t.ConsumedAt is null).TokenHash);
    }

    [Fact]
    public void Rotate_RejectsAnAlreadyConsumedToken_WithoutChangingAnything()
    {
        var first = NewHash();
        var session = AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, first, T0, Expires);
        session.Rotate(first, NewHash(), T0.AddMinutes(30));

        AssertRotationRejectedWithoutChange<InvalidOperationException>(session, first, NewHash(), T0.AddMinutes(31));
    }

    [Fact]
    public void Rotate_RejectsATokenThatIsNotLoadedOnThisSession_WithoutChangingAnything()
    {
        var session = NewSession();

        AssertRotationRejectedWithoutChange<InvalidOperationException>(session, NewHash(), NewHash(), T0.AddMinutes(30));
    }

    [Fact]
    public void Rotate_RejectsARevokedSession_WithoutChangingAnything()
    {
        var first = NewHash();
        var session = AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, first, T0, Expires);
        session.Revoke(SessionRevokeReason.UserDisabled, T0.AddMinutes(10));

        AssertRotationRejectedWithoutChange<InvalidOperationException>(session, first, NewHash(), T0.AddMinutes(30));
    }

    [Fact]
    public void Rotate_RejectsAnExpiredSession_WithoutChangingAnything()
    {
        var first = NewHash();
        var session = AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, first, T0, Expires);

        AssertRotationRejectedWithoutChange<InvalidOperationException>(session, first, NewHash(), Expires);
    }

    [Fact]
    public void Rotate_RejectsAReplacementEqualToAnyLoadedToken_WithoutChangingAnything()
    {
        var first = NewHash();
        var session = AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, first, T0, Expires);

        AssertRotationRejectedWithoutChange<ArgumentException>(session, first, first, T0.AddMinutes(30));
    }

    [Fact]
    public void Rotate_RejectsATimeBeforeTheLastRefresh_OrNotInUtc_WithoutChangingAnything()
    {
        var first = NewHash();
        var session = AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, first, T0, Expires);
        var second = NewHash();
        session.Rotate(first, second, T0.AddMinutes(30));

        AssertRotationRejectedWithoutChange<ArgumentException>(session, second, NewHash(), T0.AddMinutes(29));
        AssertRotationRejectedWithoutChange<ArgumentException>(session, second, NewHash(), T0.AddMinutes(40).ToOffset(TimeSpan.FromHours(2)));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    public void Rotate_RejectsHashesThatAreNotSha256Sized(int length)
    {
        var first = NewHash();
        var session = AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, first, T0, Expires);

        AssertRotationRejectedWithoutChange<ArgumentException>(session, first, new byte[length], T0.AddMinutes(30));
        AssertRotationRejectedWithoutChange<ArgumentException>(session, new byte[length], NewHash(), T0.AddMinutes(30));
    }

    [Theory]
    [InlineData(SessionRevokeReason.Logout)]
    [InlineData(SessionRevokeReason.Idle)]
    [InlineData(SessionRevokeReason.RefreshReuse)]
    [InlineData(SessionRevokeReason.AppAccessLost)]
    [InlineData(SessionRevokeReason.UserDisabled)]
    [InlineData(SessionRevokeReason.PasswordResetIssued)]
    [InlineData(SessionRevokeReason.PasswordResetCompleted)]
    [InlineData(SessionRevokeReason.InitialPasswordReissued)]
    [InlineData(SessionRevokeReason.PasswordChanged)]
    public void Revoke_RecordsTheReasonAndTime(SessionRevokeReason reason)
    {
        var session = NewSession();
        var at = T0.AddHours(1);

        Assert.True(session.Revoke(reason, at));

        Assert.Equal(at, session.RevokedAt);
        Assert.Equal(reason, session.RevokeReason);
    }

    [Fact]
    public void Revoke_KeepsTheFirstRevocation()
    {
        var session = NewSession();
        session.Revoke(SessionRevokeReason.PasswordChanged, T0.AddHours(1));

        Assert.False(session.Revoke(SessionRevokeReason.Logout, T0.AddHours(2)));

        Assert.Equal(T0.AddHours(1), session.RevokedAt);
        Assert.Equal(SessionRevokeReason.PasswordChanged, session.RevokeReason);
    }

    [Fact]
    public void Revoke_DoesNotConsumeTokens()
    {
        var session = NewSession();

        session.Revoke(SessionRevokeReason.Logout, T0.AddHours(1));

        Assert.Null(session.RefreshTokens.Single().ConsumedAt);
    }

    [Fact]
    public void Revoke_RejectsAnUndefinedReason_ATimeBeforeCreation_OrNotInUtc()
    {
        var session = NewSession();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Revoke((SessionRevokeReason)99, T0.AddHours(1)));
        Assert.Throws<ArgumentException>(() => session.Revoke(SessionRevokeReason.Logout, T0.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => session.Revoke(SessionRevokeReason.Logout, T0.AddHours(3).ToOffset(TimeSpan.FromHours(2))));
        Assert.Null(session.RevokedAt);
        Assert.Null(session.RevokeReason);
    }

    private static AuthSession NewSession() =>
        AuthSession.Start(UserId, ClientApp.Admin, ClientPlatform.Web, NewHash(), T0, Expires);

    private static byte[] NewHash() => RandomNumberGenerator.GetBytes(32);

    private static void AssertRotationRejectedWithoutChange<TException>(
        AuthSession session, byte[] presented, byte[] replacement, DateTimeOffset at)
        where TException : Exception
    {
        var tokensBefore = session.RefreshTokens.Select(t => (t.Id, t.ConsumedAt)).ToList();
        var lastRefreshedBefore = session.LastRefreshedAt;

        Assert.ThrowsAny<TException>(() => session.Rotate(presented, replacement, at));

        Assert.Equal(tokensBefore, session.RefreshTokens.Select(t => (t.Id, t.ConsumedAt)).ToList());
        Assert.Equal(lastRefreshedBefore, session.LastRefreshedAt);
    }
}
