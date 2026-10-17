using System.Security.Cryptography;
using Contractra.Application.Identity.Auth;
using Contractra.Infrastructure.Persistence.Throttling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// The contract v1.5 §17 policy over the shared PostgreSQL store (layers 2-4): counted at
/// admission so parallel attempts cannot exceed a limit, rejected attempts write nothing,
/// releases, and fail-closed admission when the store cannot be locked.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public class AuthThrottleIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = AuthThrottle.ToStoredPrecision(new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero).AddTicks(1234567));

    private readonly PostgresTestDatabaseFixture _fixture;

    public AuthThrottleIntegrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static byte[] NewIdentifierKey() => ThrottleKeys.IdentifierKey($"user.{Guid.NewGuid():N}");

    [Fact]
    public async Task ConcurrentAdmissionsOfOnePair_AdmitExactlyTheLimit()
    {
        await using var provider = Slice6Support.BuildProvider(_fixture);
        var throttle = provider.GetRequiredService<IAuthThrottle>();
        var key = NewIdentifierKey();

        var admissions = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => throttle.AdmitCredentialAttemptAsync(key, "203.0.113.50", T0))));

        Assert.Equal(5, admissions.Count(a => a.Status == ThrottleStatus.Admitted));
        Assert.Equal(15, admissions.Count(a => a.Status == ThrottleStatus.Throttled));
    }

    [Fact]
    public async Task RejectedAttempts_WriteNothing()
    {
        await using var provider = Slice6Support.BuildProvider(_fixture);
        var throttle = provider.GetRequiredService<IAuthThrottle>();
        var key = NewIdentifierKey();
        for (var i = 0; i < 5; i++)
        {
            await throttle.AdmitCredentialAttemptAsync(key, "203.0.113.51", T0);
        }

        var identifierRow = new AuthThrottleKey(AuthThrottleScopes.Identifier, key);
        var pairRow = new AuthThrottleKey(AuthThrottleScopes.LoginPair, AuthThrottle.PairKey(key, "203.0.113.51"));
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<AuthThrottleStore>();
        var before = (await store.GetAsync(identifierRow), await store.GetAsync(pairRow));

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(ThrottleStatus.Throttled, (await throttle.AdmitCredentialAttemptAsync(key, "203.0.113.51", T0.AddSeconds(1))).Status);
        }

        Assert.Equal(before, (await store.GetAsync(identifierRow), await store.GetAsync(pairRow)));
        Assert.Equal(5, before.Item1!.FailureCount);
    }

    [Fact]
    public async Task Success_ClearsThePair_AndReleasesTheIdentifierCount()
    {
        await using var provider = Slice6Support.BuildProvider(_fixture);
        var throttle = provider.GetRequiredService<IAuthThrottle>();
        var key = NewIdentifierKey();
        await throttle.AdmitCredentialAttemptAsync(key, "203.0.113.52", T0);
        var admitted = await throttle.AdmitCredentialAttemptAsync(key, "203.0.113.52", T0.AddSeconds(1));

        await throttle.RecordCredentialSuccessAsync(admitted.Ticket!);

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<AuthThrottleStore>();
        Assert.Null(await store.GetAsync(new AuthThrottleKey(AuthThrottleScopes.LoginPair, AuthThrottle.PairKey(key, "203.0.113.52"))));
        Assert.Equal(1, (await store.GetAsync(new AuthThrottleKey(AuthThrottleScopes.Identifier, key)))!.FailureCount);
    }

    [Fact]
    public async Task Layer3_CountsAcrossAddresses_TheSlotHoldsUnderConcurrency()
    {
        await using var provider = Slice6Support.BuildProvider(_fixture);
        var throttle = provider.GetRequiredService<IAuthThrottle>();
        var key = NewIdentifierKey();
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(ThrottleStatus.Admitted, (await throttle.AdmitCredentialAttemptAsync(key, $"203.0.114.{i}", T0)).Status);
        }

        var later = T0.AddSeconds(30);
        var racing = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Task.Run(() => throttle.AdmitCredentialAttemptAsync(key, $"203.0.115.{i}", later))));
        Assert.Single(racing, a => a.Status == ThrottleStatus.Admitted);
    }

    [Fact]
    public async Task Layer4_CountsPerUser_AndReleases()
    {
        await using var provider = Slice6Support.BuildProvider(_fixture);
        var throttle = provider.GetRequiredService<IAuthThrottle>();
        var userId = Guid.CreateVersion7();

        var admissions = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => throttle.AdmitChangePasswordAsync(userId, T0))));
        Assert.Equal(5, admissions.Count(a => a.Status == ThrottleStatus.Admitted));

        await throttle.RecordChangePasswordNotWrongAsync(admissions.First(a => a.Status == ThrottleStatus.Admitted).Ticket!);
        Assert.Equal(ThrottleStatus.Admitted, (await throttle.AdmitChangePasswordAsync(userId, T0.AddSeconds(1))).Status);
        Assert.Equal(ThrottleStatus.Throttled, (await throttle.AdmitChangePasswordAsync(userId, T0.AddSeconds(2))).Status);
        Assert.Equal(ThrottleStatus.Admitted, (await throttle.AdmitChangePasswordAsync(Guid.CreateVersion7(), T0)).Status);
    }

    [Fact]
    public async Task UnreachableStore_IsUnavailable_AndLogged()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Slice6Support.BuildProvider(_fixture, new AuthThrottleOptions { LockTimeout = TimeSpan.FromMilliseconds(300) }, logs);
        var throttle = provider.GetRequiredService<IAuthThrottle>();

        await using var blocker = new NpgsqlConnection(_fixture.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE auth_throttle_counters IN ACCESS EXCLUSIVE MODE;", blocker, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal(ThrottleStatus.Unavailable, (await throttle.AdmitCredentialAttemptAsync(NewIdentifierKey(), "203.0.113.53", T0)).Status);
        Assert.Equal(ThrottleStatus.Unavailable, (await throttle.AdmitChangePasswordAsync(Guid.CreateVersion7(), T0)).Status);
        Assert.Contains(logs.Entries, e => e.Text.Contains("Auth.ThrottleStoreUnavailable", StringComparison.Ordinal));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task StoredRows_HoldOnlyHashes()
    {
        await using var provider = Slice6Support.BuildProvider(_fixture);
        var identifier = $"Raw.Identifier.{Guid.NewGuid():N}";
        await provider.GetRequiredService<IAuthThrottle>().AdmitCredentialAttemptAsync(ThrottleKeys.IdentifierKey(identifier), "203.0.113.54", T0);

        await using var db = _fixture.CreateDbContext();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT coalesce(string_agg(t::text, ' '), '') FROM auth_throttle_counters t;";
        var text = (string)(await command.ExecuteScalarAsync())!;
        Assert.DoesNotContain(identifier, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("203.0.113.54", text, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identifier.ToLowerInvariant()))).ToLowerInvariant(), text);
    }
}
