using System.Security.Cryptography;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Persistence.Auth;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Sessions and refresh tokens on PostgreSQL (US-BE-008; auth contract v1.5
/// §3.3; ADR 0005 §4): round trip, lookup by token hash, atomic rotation,
/// concurrent rotation, rotation against revocation, and revoke-all.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public class AuthSessionPersistenceTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresTestDatabaseFixture _fixture;

    public AuthSessionPersistenceTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Session_RoundTrips_WithItsCodesAndHashedToken()
    {
        var user = await SaveUserAsync();
        var hash = NewHash();
        var session = AuthSession.Start(user, ClientApp.Employee, ClientPlatform.Mobile, hash, T0, T0.AddDays(7));
        await using (var save = _fixture.CreateDbContext())
        {
            save.AuthSessions.Add(session);
            await save.SaveChangesAsync();
        }

        await using var read = _fixture.CreateDbContext();
        var loaded = await read.AuthSessions.Include(s => s.RefreshTokens).SingleAsync(s => s.Id == session.Id);
        Assert.Equal(user, loaded.UserId);
        Assert.Equal(ClientApp.Employee, loaded.ClientApp);
        Assert.Equal(ClientPlatform.Mobile, loaded.ClientPlatform);
        Assert.Equal(T0, loaded.CreatedAt);
        Assert.Equal(T0.AddDays(7), loaded.AbsoluteExpiresAt);
        Assert.Equal(T0, loaded.LastRefreshedAt);
        Assert.Equal(hash, Assert.Single(loaded.RefreshTokens).TokenHash);

        await using var connection = await OpenAsync();
        Assert.Equal(1, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM auth_sessions WHERE id = @id AND client_app = 'employee' AND client_platform = 'mobile';", ("id", session.Id)));
    }

    [Fact]
    public async Task FindByRefreshTokenHash_LoadsOnlyThePresentedAndTheLiveToken()
    {
        var (session, hashes) = await SaveRotatedSessionAsync(rotations: 3);

        await using (var context = _fixture.CreateDbContext())
        {
            var byOld = await context.AuthSessions.FindByRefreshTokenHashAsync(hashes[1]);
            Assert.NotNull(byOld);
            Assert.Equal(session, byOld.Id);
            Assert.Equal(
                new[] { hashes[1], hashes[3] }.Select(Convert.ToHexString).Order(),
                byOld.RefreshTokens.Select(t => Convert.ToHexString(t.TokenHash)).Order());
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var byLive = await context.AuthSessions.FindByRefreshTokenHashAsync(hashes[3]);
            Assert.Equal(hashes[3], Assert.Single(byLive!.RefreshTokens).TokenHash);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            Assert.Null(await context.AuthSessions.FindByRefreshTokenHashAsync(NewHash()));
            await Assert.ThrowsAsync<ArgumentException>(() => context.AuthSessions.FindByRefreshTokenHashAsync(new byte[16]));
        }
    }

    [Fact]
    public async Task Rotate_PersistsTheConsumedTokenTheReplacementAndTheRefreshTime_InOneSave()
    {
        var (session, hashes) = await SaveRotatedSessionAsync(rotations: 0);
        var next = NewHash();

        await using (var context = _fixture.CreateDbContext())
        {
            var loaded = await context.AuthSessions.FindByRefreshTokenHashAsync(hashes[0]);
            loaded!.Rotate(hashes[0], next, T0.AddMinutes(30));
            await context.SaveChangesAsync();
        }

        await using var connection = await OpenAsync();
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @id AND consumed_at = @at;", ("id", session), ("at", T0.AddMinutes(30))));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @id AND consumed_at IS NULL AND token_hash = @hash;", ("id", session), ("hash", next)));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM auth_sessions WHERE id = @id AND last_refreshed_at = @at;", ("id", session), ("at", T0.AddMinutes(30))));
    }

    [Fact]
    public async Task ConcurrentRotationsOfOneToken_ExactlyOneWins_AndLosersChangeNothing()
    {
        var (session, hashes) = await SaveRotatedSessionAsync(rotations: 0);
        const int Rotations = 8;
        var replacements = Enumerable.Range(0, Rotations).Select(_ => NewHash()).ToArray();

        // Every context loads and rotates before any saves, so all of them pass
        // the Domain check and the database alone decides the race.
        var rotated = 0;
        var saveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = await Concurrently.RunAsync(Rotations, async i =>
        {
            await using var context = _fixture.CreateDbContext();
            var loaded = await context.AuthSessions.FindByRefreshTokenHashAsync(hashes[0]);
            loaded!.Rotate(hashes[0], replacements[i], T0.AddMinutes(30));
            if (Interlocked.Increment(ref rotated) == Rotations)
            {
                saveGate.SetResult();
            }

            await saveGate.Task;
            await context.SaveChangesAsync();
        });

        Assert.Equal(1, errors.Count(e => e is null));
        Assert.All(errors.Where(e => e is not null), e => Assert.IsType<DbUpdateConcurrencyException>(e));

        var winner = replacements[Array.FindIndex(errors, e => e is null)];
        await using var connection = await OpenAsync();
        Assert.Equal(2, await CountAsync(connection, "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @id;", ("id", session)));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @id AND consumed_at IS NULL AND token_hash = @hash;", ("id", session), ("hash", winner)));
    }

    [Fact]
    public async Task ARotation_FailingOnItsInsert_ChangesNothing()
    {
        var (session, hashes) = await SaveRotatedSessionAsync(rotations: 0);
        var (_, otherHashes) = await SaveRotatedSessionAsync(rotations: 0);

        await using (var context = _fixture.CreateDbContext())
        {
            var loaded = await context.AuthSessions.FindByRefreshTokenHashAsync(hashes[0]);
            loaded!.Rotate(hashes[0], otherHashes[0], T0.AddMinutes(30));
            await PostgresAssert.FailsWithAsync(() => context.SaveChangesAsync(), "23505", "ux_refresh_tokens_token_hash");
        }

        await using var connection = await OpenAsync();
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @id AND consumed_at IS NULL;", ("id", session)));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @id;", ("id", session)));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM auth_sessions WHERE id = @id AND last_refreshed_at = @at;", ("id", session), ("at", T0)));
    }

    [Fact]
    public async Task ARevocationCommittedDuringARotation_MakesTheRotationFail()
    {
        var (session, hashes) = await SaveRotatedSessionAsync(rotations: 0);
        await using var rotating = _fixture.CreateDbContext();
        var loaded = await rotating.AuthSessions.FindByRefreshTokenHashAsync(hashes[0]);

        await using (var revoking = _fixture.CreateDbContext())
        await using (var revokeTransaction = await revoking.Database.BeginTransactionAsync())
        {
            Assert.Equal(1, await revoking.AuthSessions.RevokeAllActiveAsync(loaded!.UserId, SessionRevokeReason.UserDisabled, T0.AddMinutes(10)));
            await revokeTransaction.CommitAsync();
        }

        loaded!.Rotate(hashes[0], NewHash(), T0.AddMinutes(30));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => rotating.SaveChangesAsync());

        await using var connection = await OpenAsync();
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @id;", ("id", session)));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM auth_sessions WHERE id = @id AND revoke_reason = 'user_disabled';", ("id", session)));
    }

    [Fact]
    public async Task ARevocationWaitingOnARotation_StillRevokesOnceTheRotationCommits()
    {
        var (session, hashes) = await SaveRotatedSessionAsync(rotations: 0);
        Guid userId;

        await using var rotating = _fixture.CreateDbContext();
        await using var transaction = await rotating.Database.BeginTransactionAsync();
        var loaded = await rotating.AuthSessions.FindByRefreshTokenHashAsync(hashes[0]);
        userId = loaded!.UserId;
        loaded.Rotate(hashes[0], NewHash(), T0.AddMinutes(30));
        await rotating.SaveChangesAsync();

        // The rotation holds the session row lock; the revocation must wait for it.
        await using var revoking = _fixture.CreateDbContext();
        await using var revokeTransaction = await revoking.Database.BeginTransactionAsync();
        var revocation = revoking.AuthSessions.RevokeAllActiveAsync(userId, SessionRevokeReason.PasswordChanged, T0.AddMinutes(31));
        await WaitUntilAWriterIsBlockedAsync();
        Assert.False(revocation.IsCompleted);

        await transaction.CommitAsync();
        Assert.Equal(1, await revocation);
        await revokeTransaction.CommitAsync();

        await using var connection = await OpenAsync();
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM auth_sessions WHERE id = @id AND revoke_reason = 'password_changed';", ("id", session)));
        Assert.Equal(2, await CountAsync(connection, "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @id;", ("id", session)));
    }

    [Fact]
    public async Task RevokeAllActive_RevokesOnlyTheUsersActiveSessions_ExceptTheOneKept_AndKeepsEarlierReasons()
    {
        var user = await SaveUserAsync();
        var otherUser = await SaveUserAsync();
        var kept = await SaveSessionAsync(user);
        var revokedEarlier = await SaveSessionAsync(user);
        var active1 = await SaveSessionAsync(user);
        var active2 = await SaveSessionAsync(user);
        var otherUsers = await SaveSessionAsync(otherUser);
        await using (var context = _fixture.CreateDbContext())
        {
            var earlier = await context.AuthSessions.SingleAsync(s => s.Id == revokedEarlier);
            earlier.Revoke(SessionRevokeReason.Logout, T0.AddMinutes(5));
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateDbContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.Equal(2, await context.AuthSessions.RevokeAllActiveAsync(user, SessionRevokeReason.PasswordChanged, T0.AddHours(1), exceptSessionId: kept));
            await transaction.CommitAsync();
        }

        await using var connection = await OpenAsync();
        Assert.Equal("password_changed", await ReasonAsync(connection, active1));
        Assert.Equal("password_changed", await ReasonAsync(connection, active2));
        Assert.Equal("logout", await ReasonAsync(connection, revokedEarlier));
        Assert.Null(await ReasonAsync(connection, kept));
        Assert.Null(await ReasonAsync(connection, otherUsers));
    }

    [Fact]
    public async Task RevokeAllActive_RejectsAnUndefinedReasonOrANonUtcTime()
    {
        await using var context = _fixture.CreateDbContext();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => context.AuthSessions.RevokeAllActiveAsync(Guid.CreateVersion7(), (SessionRevokeReason)99, T0));
        await Assert.ThrowsAsync<ArgumentException>(() => context.AuthSessions.RevokeAllActiveAsync(Guid.CreateVersion7(), SessionRevokeReason.Logout, T0.ToOffset(TimeSpan.FromHours(2))));
    }

    [Fact]
    public async Task RevokeAllActive_WithoutAnOpenTransaction_ThrowsAndChangesNothing()
    {
        var user = await SaveUserAsync();
        await SaveSessionAsync(user);

        await using var context = _fixture.CreateDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.AuthSessions.RevokeAllActiveAsync(user, SessionRevokeReason.UserDisabled, T0));

        await using var connection = await OpenAsync();
        Assert.Equal(0, await CountAsync(connection, "SELECT COUNT(*) FROM auth_sessions WHERE user_id = @id AND revoked_at IS NOT NULL;", ("id", user)));
    }

    private async Task<Guid> SaveUserAsync()
    {
        var user = TestUsers.NewUser($"Session.{Guid.NewGuid():N}");
        await using var context = _fixture.CreateDbContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SaveSessionAsync(Guid userId)
    {
        var session = AuthSession.Start(userId, ClientApp.Admin, ClientPlatform.Web, NewHash(), T0, T0.AddDays(7));
        await using var context = _fixture.CreateDbContext();
        context.AuthSessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
    }

    /// <summary>A saved session whose token hashes, oldest first, are returned; the last one is live.</summary>
    private async Task<(Guid Session, List<byte[]> Hashes)> SaveRotatedSessionAsync(int rotations)
    {
        var userId = await SaveUserAsync();
        var hashes = new List<byte[]> { NewHash() };
        var session = AuthSession.Start(userId, ClientApp.Employee, ClientPlatform.Mobile, hashes[0], T0, T0.AddDays(7));
        for (var i = 1; i <= rotations; i++)
        {
            hashes.Add(NewHash());
            session.Rotate(hashes[i - 1], hashes[i], T0.AddMinutes(i));
        }

        await using var context = _fixture.CreateDbContext();
        context.AuthSessions.Add(session);
        await context.SaveChangesAsync();
        return (session.Id, hashes);
    }

    private async Task WaitUntilAWriterIsBlockedAsync()
    {
        await using var connection = await OpenAsync();
        for (var i = 0; i < 250; i++)
        {
            if (await CountAsync(connection, "SELECT COUNT(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock';") > 0)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new Xunit.Sdk.XunitException("No writer was blocked on a lock within 5 seconds.");
    }

    private static async Task<string?> ReasonAsync(NpgsqlConnection connection, Guid session)
    {
        await using var command = new NpgsqlCommand("SELECT revoke_reason FROM auth_sessions WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", session);
        return await command.ExecuteScalarAsync() as string;
    }

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
