using System.Security.Cryptography;
using Contractra.Domain.Identity;
using static Contractra.Domain.Tests.Identity.TestUsers;

namespace Contractra.Domain.Tests.Identity;

/// <summary>
/// Password-reset tickets (Backend Sprint 2, US-BE-008; auth contract v1.5
/// §5.6, §9.6, §10A; ADR 0005 §3.3).
/// </summary>
public class PasswordResetTicketTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid AdminId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Expires = T0.AddHours(24);

    [Fact]
    public void Issue_CreatesAnOpenTicket()
    {
        var hash = NewHash();

        var ticket = PasswordResetTicket.Issue(UserId, hash, T0, Expires, AdminId, "email");

        Assert.NotEqual(Guid.Empty, ticket.Id);
        Assert.Equal(UserId, ticket.UserId);
        Assert.Equal(hash, ticket.CodeHash);
        Assert.Equal(T0, ticket.IssuedAt);
        Assert.Equal(Expires, ticket.ExpiresAt);
        Assert.Equal(AdminId, ticket.IssuedByUserId);
        Assert.Equal("email", ticket.Channel);
        Assert.Null(ticket.ConsumedAt);
        Assert.Null(ticket.SupersededAt);
        Assert.True(ticket.IsOpen);
    }

    [Fact]
    public void Issue_BySystem_HasNoIssuingUser()
    {
        var ticket = PasswordResetTicket.Issue(UserId, NewHash(), T0, Expires, issuedByUserId: null, "operator");

        Assert.Null(ticket.IssuedByUserId);
    }

    [Fact]
    public void Issue_CopiesTheHash()
    {
        var hash = NewHash();
        var ticket = PasswordResetTicket.Issue(UserId, hash, T0, Expires, AdminId, "operator");

        hash[0] ^= 0xFF;

        Assert.NotEqual(hash, ticket.CodeHash);
    }

    [Fact]
    public void CodeHash_ReturnsACopy_SoChangingItCannotChangeTheStoredHash()
    {
        var hash = NewHash();
        var ticket = PasswordResetTicket.Issue(UserId, hash, T0, Expires, AdminId, "operator");

        ticket.CodeHash[0] ^= 0xFF;

        Assert.Equal(hash, ticket.CodeHash);
    }

    [Fact]
    public void Issue_RejectsInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => PasswordResetTicket.Issue(Guid.Empty, NewHash(), T0, Expires, AdminId, "operator"));
        Assert.Throws<ArgumentNullException>(() => PasswordResetTicket.Issue(UserId, null!, T0, Expires, AdminId, "operator"));
        Assert.Throws<ArgumentException>(() => PasswordResetTicket.Issue(UserId, new byte[31], T0, Expires, AdminId, "operator"));
        Assert.Throws<ArgumentException>(() => PasswordResetTicket.Issue(UserId, NewHash(), T0, T0, AdminId, "operator"));
        Assert.Throws<ArgumentException>(() => PasswordResetTicket.Issue(UserId, NewHash(), T0.ToOffset(TimeSpan.FromHours(2)), Expires, AdminId, "operator"));
        Assert.Throws<ArgumentException>(() => PasswordResetTicket.Issue(UserId, NewHash(), T0, Expires.ToOffset(TimeSpan.FromHours(2)), AdminId, "operator"));
        Assert.Throws<ArgumentException>(() => PasswordResetTicket.Issue(UserId, NewHash(), T0, Expires, Guid.Empty, "operator"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" operator")]
    [InlineData("operator ")]
    [InlineData("a-channel-name-of-thirty-three-ch")]
    public void Issue_RejectsAMissingPaddedOrOverlongChannel(string? channel)
    {
        Assert.ThrowsAny<ArgumentException>(() => PasswordResetTicket.Issue(UserId, NewHash(), T0, Expires, AdminId, channel!));
    }

    [Fact]
    public void Issue_AcceptsAChannelOfExactlyTheMaximumLength()
    {
        var channel = new string('c', PasswordResetTicket.MaxChannelLength);

        Assert.Equal(channel, PasswordResetTicket.Issue(UserId, NewHash(), T0, Expires, AdminId, channel).Channel);
    }

    [Fact]
    public void IsValidAt_FromIssueUntilJustBeforeExpiry_WhileOpen()
    {
        var ticket = NewTicket();

        Assert.False(ticket.IsValidAt(T0.AddTicks(-1)));
        Assert.True(ticket.IsValidAt(T0));
        Assert.True(ticket.IsValidAt(Expires.AddTicks(-1)));
        Assert.False(ticket.IsValidAt(Expires));
    }

    [Fact]
    public void Consume_WhileValid_ClosesTheTicket()
    {
        var ticket = NewTicket();
        var at = T0.AddHours(1);

        ticket.Consume(at);

        Assert.Equal(at, ticket.ConsumedAt);
        Assert.False(ticket.IsOpen);
        Assert.False(ticket.IsValidAt(at));
    }

    [Fact]
    public void Consume_AtOrAfterExpiry_IsRejected_AndTheTicketStaysOpen()
    {
        var ticket = NewTicket();

        Assert.Throws<InvalidOperationException>(() => ticket.Consume(Expires));
        Assert.Throws<InvalidOperationException>(() => ticket.Consume(Expires.AddHours(1)));

        Assert.Null(ticket.ConsumedAt);
        Assert.True(ticket.IsOpen);
    }

    [Fact]
    public void Consume_Twice_IsRejected()
    {
        var ticket = NewTicket();
        ticket.Consume(T0.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => ticket.Consume(T0.AddHours(2)));

        Assert.Equal(T0.AddHours(1), ticket.ConsumedAt);
    }

    [Fact]
    public void Consume_ASupersededTicket_IsRejected()
    {
        var ticket = NewTicket();
        ticket.Supersede(T0.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => ticket.Consume(T0.AddHours(2)));

        Assert.Null(ticket.ConsumedAt);
    }

    [Fact]
    public void Consume_RejectsATimeBeforeIssue_OrNotInUtc()
    {
        var ticket = NewTicket();

        Assert.Throws<ArgumentException>(() => ticket.Consume(T0.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => ticket.Consume(T0.AddHours(1).ToOffset(TimeSpan.FromHours(2))));
        Assert.True(ticket.IsOpen);
    }

    [Fact]
    public void Supersede_ClosesAnOpenTicket_EvenAfterItExpired()
    {
        var ticket = NewTicket();
        var at = Expires.AddHours(5);

        ticket.Supersede(at);

        Assert.Equal(at, ticket.SupersededAt);
        Assert.False(ticket.IsOpen);
    }

    [Fact]
    public void Supersede_AClosedTicket_IsRejected()
    {
        var consumed = NewTicket();
        consumed.Consume(T0.AddHours(1));
        var superseded = NewTicket();
        superseded.Supersede(T0.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => consumed.Supersede(T0.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() => superseded.Supersede(T0.AddHours(2)));

        Assert.Null(consumed.SupersededAt);
        Assert.Equal(T0.AddHours(1), superseded.SupersededAt);
    }

    [Fact]
    public void Supersede_RejectsATimeBeforeIssue_OrNotInUtc()
    {
        var ticket = NewTicket();

        Assert.Throws<ArgumentException>(() => ticket.Supersede(T0.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => ticket.Supersede(T0.AddHours(1).ToOffset(TimeSpan.FromHours(2))));
        Assert.True(ticket.IsOpen);
    }

    private static PasswordResetTicket NewTicket() =>
        PasswordResetTicket.Issue(UserId, NewHash(), T0, Expires, AdminId, "operator");

    private static byte[] NewHash() => RandomNumberGenerator.GetBytes(32);
}
