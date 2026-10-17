using System.Security.Cryptography;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Persistence.Auth;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Reset tickets on PostgreSQL (US-BE-008; auth contract v1.5 §5.6, §9.6,
/// §10A; ADR 0005 §3.3, §4): issue = supersede + insert in one transaction,
/// one open ticket per user under concurrency, single-winner consumption, and
/// failed verification writing nothing.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public class PasswordResetTicketPersistenceTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresTestDatabaseFixture _fixture;

    public PasswordResetTicketPersistenceTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Issue_TheFirstTicket_IsOpen_AndRoundTrips()
    {
        var user = await SaveUserAsync();
        var hash = NewHash();
        var ticket = PasswordResetTicket.Issue(user, hash, T0, T0.AddHours(24), issuedByUserId: null, "operator");

        await using (var context = _fixture.CreateDbContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.Equal(0, await context.IssuePasswordResetTicketAsync(ticket));
            await transaction.CommitAsync();
        }

        await using var read = _fixture.CreateDbContext();
        var open = await read.PasswordResetTickets.FindOpenAsync(user);
        Assert.NotNull(open);
        Assert.Equal(ticket.Id, open.Id);
        Assert.Equal(hash, open.CodeHash);
        Assert.Equal(T0, open.IssuedAt);
        Assert.Equal(T0.AddHours(24), open.ExpiresAt);
        Assert.Null(open.IssuedByUserId);
        Assert.Equal("operator", open.Channel);
    }

    [Fact]
    public async Task Issue_SupersedesTheOpenTicket_EvenAnExpiredOne_LeavingExactlyOneOpen()
    {
        var user = await SaveUserAsync();
        var admin = await SaveUserAsync();
        var first = await IssueAsync(user, T0);

        // Issued after the first one expired: it still counts as open until superseded.
        var issuedAt = T0.AddHours(30);
        var second = PasswordResetTicket.Issue(user, NewHash(), issuedAt, issuedAt.AddHours(24), admin, "operator");
        await using (var context = _fixture.CreateDbContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.Equal(1, await context.IssuePasswordResetTicketAsync(second));
            await transaction.CommitAsync();
        }

        await using var connection = await OpenAsync();
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM password_reset_tickets WHERE id = @id AND superseded_at = @at AND consumed_at IS NULL;", ("id", first), ("at", issuedAt)));
        Assert.Equal(1, await OpenCountAsync(connection, user));

        await using var read = _fixture.CreateDbContext();
        Assert.Equal(second.Id, (await read.PasswordResetTickets.FindOpenAsync(user))!.Id);
    }

    [Fact]
    public async Task Issue_RejectsATicketThatIsNotOpen()
    {
        var ticket = PasswordResetTicket.Issue(await SaveUserAsync(), NewHash(), T0, T0.AddHours(24), null, "operator");
        ticket.Consume(T0.AddHours(1));

        await using var context = _fixture.CreateDbContext();
        await Assert.ThrowsAsync<ArgumentException>(() => context.IssuePasswordResetTicketAsync(ticket));
    }

    [Fact]
    public async Task ConcurrentIssuesForOneUser_LeaveExactlyOneOpenTicket()
    {
        var user = await SaveUserAsync();
        await IssueAsync(user, T0);
        const int Issues = 6;

        // One issue time for all: superseded_at may never precede the superseded
        // ticket's issued_at, so out-of-order clocks would be refused (23514).
        var errors = await Concurrently.RunAsync(Issues, async _ =>
        {
            await using var context = _fixture.CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.IssuePasswordResetTicketAsync(
                PasswordResetTicket.Issue(user, NewHash(), T0.AddMinutes(10), T0.AddHours(24), null, "operator"));
            await transaction.CommitAsync();
        });

        Assert.True(errors.Count(e => e is null) >= 1);
        Assert.All(errors.Where(e => e is not null), e => Assert.True(Concurrently.IsConflict(e!), e!.ToString()));

        await using var connection = await OpenAsync();
        Assert.Equal(1, await OpenCountAsync(connection, user));
        Assert.Equal(1 + errors.Count(e => e is null), await CountAsync(connection, "SELECT COUNT(*) FROM password_reset_tickets WHERE user_id = @user;", ("user", user)));
    }

    [Fact]
    public async Task ConcurrentCompletionsOfOneTicket_ExactlyOneConsumesIt()
    {
        var user = await SaveUserAsync();
        var ticket = await IssueAsync(user, T0);
        const int Completions = 5;

        var errors = await Concurrently.RunAsync(Completions, async i =>
        {
            await using var context = _fixture.CreateDbContext();
            var open = await context.PasswordResetTickets.FindOpenAsync(user);
            open!.Consume(T0.AddMinutes(10 + i));
            await context.SaveChangesAsync();
        });

        Assert.Equal(1, errors.Count(e => e is null));
        Assert.All(errors.Where(e => e is not null), e => Assert.IsType<DbUpdateConcurrencyException>(e));

        var winnerTime = T0.AddMinutes(10 + Array.FindIndex(errors, e => e is null));
        await using var connection = await OpenAsync();
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM password_reset_tickets WHERE id = @id AND consumed_at = @at;", ("id", ticket), ("at", winnerTime)));
    }

    [Fact]
    public async Task ACompletionLosingToANewIssue_FailsAndLeavesTheTicketSuperseded()
    {
        var user = await SaveUserAsync();
        var first = await IssueAsync(user, T0);

        await using var completing = _fixture.CreateDbContext();
        var open = await completing.PasswordResetTickets.FindOpenAsync(user);
        await IssueAsync(user, T0.AddMinutes(5));

        open!.Consume(T0.AddMinutes(6));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => completing.SaveChangesAsync());

        await using var connection = await OpenAsync();
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM password_reset_tickets WHERE id = @id AND consumed_at IS NULL AND superseded_at IS NOT NULL;", ("id", first)));
    }

    [Fact]
    public async Task AFailedCodeCheck_WritesNothingToTheTicket()
    {
        var user = await SaveUserAsync();
        var ticket = await IssueAsync(user, T0);
        await using var connection = await OpenAsync();
        var before = await RowSnapshotAsync(connection, ticket);

        await using (var context = _fixture.CreateDbContext())
        {
            var open = await context.PasswordResetTickets.FindOpenAsync(user);

            // What the Application does on a wrong or expired code: compare, refuse, no mutation.
            Assert.False(CryptographicOperations.FixedTimeEquals(open!.CodeHash, NewHash()));
            Assert.False(open.IsValidAt(open.ExpiresAt));
            Assert.Equal(0, await context.SaveChangesAsync());
        }

        Assert.Equal(before, await RowSnapshotAsync(connection, ticket));
    }

    [Fact]
    public async Task IssueInsideACallerTransaction_ThatIsNotCommitted_LeavesTheOldTicketOpen()
    {
        var user = await SaveUserAsync();
        var first = await IssueAsync(user, T0);

        await using (var context = _fixture.CreateDbContext())
        await using (await context.Database.BeginTransactionAsync())
        {
            await context.IssuePasswordResetTicketAsync(
                PasswordResetTicket.Issue(user, NewHash(), T0.AddMinutes(5), T0.AddHours(24), null, "operator"));

            // A later step of the same use case fails; E25: the transaction is never committed.
        }

        await using var connection = await OpenAsync();
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM password_reset_tickets WHERE user_id = @user;", ("user", user)));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM password_reset_tickets WHERE id = @id AND superseded_at IS NULL;", ("id", first)));
    }

    [Fact]
    public async Task FindOpen_ReturnsNull_WhenTheUserHasNoOpenTicket()
    {
        var user = await SaveUserAsync();

        await using var context = _fixture.CreateDbContext();
        Assert.Null(await context.PasswordResetTickets.FindOpenAsync(user));
    }

    [Fact]
    public async Task Issue_WithoutAnOpenTransaction_ThrowsAndChangesNothing()
    {
        var user = await SaveUserAsync();
        var ticket = PasswordResetTicket.Issue(user, NewHash(), T0, T0.AddHours(24), null, "operator");

        await using var context = _fixture.CreateDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.IssuePasswordResetTicketAsync(ticket));

        await using var connection = await OpenAsync();
        Assert.Equal(0, await CountAsync(connection, "SELECT COUNT(*) FROM password_reset_tickets WHERE user_id = @user;", ("user", user)));
    }

    private async Task<Guid> IssueAsync(Guid user, DateTimeOffset issuedAt)
    {
        var ticket = PasswordResetTicket.Issue(user, NewHash(), issuedAt, issuedAt.AddHours(24), null, "operator");
        await using var context = _fixture.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.IssuePasswordResetTicketAsync(ticket);
        await transaction.CommitAsync();
        return ticket.Id;
    }

    private async Task<Guid> SaveUserAsync()
    {
        var user = TestUsers.NewUser($"Reset.{Guid.NewGuid():N}");
        await using var context = _fixture.CreateDbContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Every column plus xmin: unchanged only if the row was never updated.</summary>
    private static async Task<string> RowSnapshotAsync(NpgsqlConnection connection, Guid ticket)
    {
        await using var command = new NpgsqlCommand(
            "SELECT row_to_json(t)::text || '|' || t.xmin::text FROM password_reset_tickets t WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", ticket);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static Task<long> OpenCountAsync(NpgsqlConnection connection, Guid user) =>
        CountAsync(connection, "SELECT COUNT(*) FROM password_reset_tickets WHERE user_id = @user AND consumed_at IS NULL AND superseded_at IS NULL;", ("user", user));

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static Task<long> CountAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters) =>
        PostgresAssert.CountAsync(connection, null, sql, parameters);

    private static byte[] NewHash() => RandomNumberGenerator.GetBytes(32);
}
