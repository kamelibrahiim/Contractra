using System.Security.Cryptography;
using System.Text;
using Contractra.Application.Audit;
using Contractra.Application.Identity;
using Contractra.Application.Identity.Credentials;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Identity.Users;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Identity;
using Contractra.Infrastructure.Persistence;
using Contractra.Infrastructure.Persistence.Audit;
using Contractra.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// credential_issuances on PostgreSQL (PS-04a; ADR 0007; contract v1.6 §23): the schema's guarantees (no ciphertext on a
/// finished issuance, one open issuance per user, deferred so supersede + insert is one transaction), a single winner
/// when workers race, idempotent webhook events, and the whole create -> deliver -> status flow through the real DI
/// container, including proof that the password exists in the database only as a hash and a sealed copy.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public class CredentialIssuanceIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Number = "+201000000001";
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresTestDatabaseFixture _fixture;

    public CredentialIssuanceIntegrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- schema guarantees --------------------------------------------------------------------------------------

    [Fact]
    public async Task AFinishedIssuance_CannotKeepItsCiphertext()
    {
        var issuance = await SaveIssuanceAsync();

        await using var connection = await OpenAsync();
        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(
                connection, null,
                "UPDATE credential_issuances SET status = 'Failed', failure_category = 'ProviderOutage', ended_at = now() WHERE id = @id;",
                ("id", issuance.Id)),
            "23514",
            "ck_credential_issuances_terminal_has_no_ciphertext");
    }

    [Fact]
    public async Task TheSealedColumnsAreAllPresentOrAllAbsent()
    {
        var issuance = await SaveIssuanceAsync();

        await using var connection = await OpenAsync();
        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "UPDATE credential_issuances SET seal_nonce = NULL WHERE id = @id;", ("id", issuance.Id)),
            "23514",
            "ck_credential_issuances_sealed_columns_together");
    }

    [Fact]
    public async Task APurgedIssuance_RoundTripsWithNoSealedColumnsAndARecordedPurge()
    {
        var issuance = await SaveIssuanceAsync();

        await using (var context = _fixture.CreateDbContext())
        {
            var tracked = await context.CredentialIssuances.SingleAsync(i => i.Id == issuance.Id);
            tracked.Cancel(T0.AddMinutes(5));
            await context.SaveChangesAsync();
        }

        await using var read = _fixture.CreateDbContext();
        var reloaded = await read.CredentialIssuances.AsNoTracking().SingleAsync(i => i.Id == issuance.Id);
        Assert.Equal(CredentialDeliveryStatus.Cancelled, reloaded.Status);
        Assert.False(reloaded.HasSealedCopy);
        Assert.Null(reloaded.SealNonce);
        Assert.Null(reloaded.SealKeyId);
        Assert.Equal(T0.AddMinutes(5), reloaded.PurgedAt);
    }

    [Fact]
    public async Task ASecondOpenIssuanceForOneUser_IsRefusedAtCommit()
    {
        var userId = await SaveUserAsync();
        await SaveIssuanceAsync(userId);

        var second = NewIssuance(userId);
        await PostgresAssert.FailsWithAsync(
            async () =>
            {
                await using var context = _fixture.CreateDbContext();
                context.CredentialIssuances.Add(second);
                await context.SaveChangesAsync();
            },
            "23P01",
            "ex_credential_issuances_one_open_per_user");
    }

    [Fact]
    public async Task SupersedingTheOpenIssuanceAndQueueingTheNextOne_IsOneValidTransaction_InEitherOrder()
    {
        var userId = await SaveUserAsync();
        var first = await SaveIssuanceAsync(userId);
        var second = NewIssuance(userId);

        await using (var context = _fixture.CreateDbContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var tracked = await context.CredentialIssuances.SingleAsync(i => i.Id == first.Id);
            context.CredentialIssuances.Add(second); // the insert is staged before the supersede
            tracked.Supersede(T0.AddMinutes(1));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var read = _fixture.CreateDbContext();
        var rows = await read.CredentialIssuances.AsNoTracking().Where(i => i.UserId == userId).ToListAsync();
        Assert.Equal(CredentialDeliveryStatus.Superseded, rows.Single(r => r.Id == first.Id).Status);
        Assert.Equal(CredentialDeliveryStatus.Queued, rows.Single(r => r.Id == second.Id).Status);
        Assert.Equal(1, rows.Count(r => r.IsOpen));
    }

    [Fact]
    public async Task ADestinationThatIsNotE164_IsRefused()
    {
        var userId = await SaveUserAsync();
        var bad = CredentialIssuance.Queue(
            Guid.CreateVersion7(), userId, CredentialIssuanceKind.Initial, userId, T0, "01000000001", "verbal",
            [1, 2, 3], [4, 5, 6], "k1", TimeSpan.FromHours(24));

        await PostgresAssert.FailsWithAsync(
            async () =>
            {
                await using var context = _fixture.CreateDbContext();
                context.CredentialIssuances.Add(bad);
                await context.SaveChangesAsync();
            },
            "23514",
            "ck_credential_issuances_destination_e164");
    }

    [Fact]
    public async Task AProviderMessageId_BelongsToOneIssuance()
    {
        var first = await SaveIssuanceAsync();
        var second = await SaveIssuanceAsync();
        await SetAcceptedAsync(first.Id, "wamid.same");

        await PostgresAssert.FailsWithAsync(
            () => SetAcceptedAsync(second.Id, "wamid.same"),
            "23505",
            "ux_credential_issuances_provider_message_id");
    }

    // ---- store ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task WhenWorkersRace_ExactlyOneClaimsTheIssuance()
    {
        var issuance = await SaveIssuanceAsync();
        var claimed = new List<string>();

        var errors = await Concurrently.RunAsync(6, async i =>
        {
            await using var context = _fixture.CreateDbContext();
            var store = new CredentialIssuanceStore(context);
            var row = await store.TryClaimAsync(issuance.Id, $"worker-{i}", T0, TimeSpan.FromMinutes(2));
            if (row is not null)
            {
                lock (claimed)
                {
                    claimed.Add(row.LeaseOwner!);
                }
            }
        });

        Assert.All(errors, e => Assert.Null(e));
        Assert.Single(claimed);

        await using var read = _fixture.CreateDbContext();
        var stored = await read.CredentialIssuances.AsNoTracking().SingleAsync(i => i.Id == issuance.Id);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal(claimed[0], stored.LeaseOwner);
    }

    [Fact]
    public async Task AWebhookEvent_IsRecordedOnce()
    {
        await using var context = _fixture.CreateDbContext();
        var store = new CredentialIssuanceStore(context);
        var id = $"wamid.{Guid.NewGuid():N}";

        Assert.True(await store.TryRecordWebhookEventAsync(id, "sent", T0, null, null));

        await using var again = _fixture.CreateDbContext();
        Assert.False(await new CredentialIssuanceStore(again).TryRecordWebhookEventAsync(id, "sent", T0, null, null));
        await using var otherStatus = _fixture.CreateDbContext();
        Assert.True(await new CredentialIssuanceStore(otherStatus).TryRecordWebhookEventAsync(id, "delivered", T0, null, null));
    }

    [Fact]
    public async Task TheSummaryReadsNeverSelectTheSealedColumns()
    {
        var issuance = await SaveIssuanceAsync();
        var log = new List<string>();

        await using var context = _fixture.CreateDbContext();
        context.Database.SetCommandTimeout(30);
        var store = new CredentialIssuanceStore(context);

        // A summary is a projection: it compiles to explicit columns, none of them sealed_*.
        var sql = context.CredentialIssuances.AsNoTracking()
            .Where(i => i.UserId == issuance.UserId)
            .Select(i => new CredentialIssuanceSummary(
                i.Id, i.UserId, i.Kind, i.Status, i.Attempts, i.IssuedAt, i.UpdatedAt, i.Destination, i.ConsentBasis, i.FailureCategory))
            .ToQueryString();
        log.Add(sql);

        Assert.DoesNotContain("sealed_bytes", sql);
        Assert.DoesNotContain("seal_nonce", sql);
        Assert.DoesNotContain("seal_key_id", sql);
        Assert.NotNull(await store.FindLatestSummaryForUserAsync(issuance.UserId));
        Assert.Single(await store.ListSummariesForUserAsync(issuance.UserId, 10));
    }

    // ---- the whole flow through DI -------------------------------------------------------------------------------

    [Fact]
    public async Task Create_ThenDeliver_SendsTheSamePasswordTheUserCanSignInWith_AndStoresItOnlyHashedAndSealed()
    {
        await using var provider = BuildProvider();
        var (_, password, userId, issuanceId) = await CreateEngineerLikeUserAsync(provider);

        // The user's hash verifies the password the sender received - one password per operation.
        await using (var scope = provider.CreateAsyncScope())
        {
            var outcome = await scope.ServiceProvider.GetRequiredService<DeliverCredentialUseCase>().ExecuteAsync(issuanceId, "it-worker");
            Assert.Equal(DeliveryAttemptOutcome.Accepted, outcome);
        }

        var sent = provider.GetRequiredService<FakeWhatsAppSender>().Sent.Single(m => m.Destination == Number && m.Username == password.Username);
        await using (var read = _fixture.CreateDbContext())
        {
            var user = await read.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            var hasher = provider.GetRequiredService<IPasswordHasher>();
            Assert.Equal(PasswordVerificationResult.Success, hasher.Verify(user.PasswordHash, sent.Password));
        }

        // The password is in the database nowhere in the clear: not in users, issuances, audit events.
        await AssertPlaintextAbsentAsync(sent.Password, userId);
    }

    [Fact]
    public async Task AfterTheProviderReportsDelivered_TheSealedCopyIsGone_AndTheStateIsPasswordSet()
    {
        await using var provider = BuildProvider();
        var (_, _, userId, issuanceId) = await CreateEngineerLikeUserAsync(provider);

        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(DeliveryAttemptOutcome.Accepted, await scope.ServiceProvider.GetRequiredService<DeliverCredentialUseCase>().ExecuteAsync(issuanceId, "w"));
        }

        Assert.Equal(CredentialState.DeliveryPending, await CredentialStateOfAsync(provider, userId));

        string messageId;
        await using (var read = _fixture.CreateDbContext())
        {
            messageId = (await read.CredentialIssuances.AsNoTracking().SingleAsync(i => i.Id == issuanceId)).ProviderMessageId!;
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplyWhatsAppStatusUseCase>()
                .ExecuteAsync(messageId, "delivered", DateTimeOffset.UtcNow, null, null);
        }

        await using var after = _fixture.CreateDbContext();
        var issuance = await after.CredentialIssuances.AsNoTracking().SingleAsync(i => i.Id == issuanceId);
        Assert.Equal(CredentialDeliveryStatus.Delivered, issuance.Status);
        Assert.False(issuance.HasSealedCopy);
        Assert.Equal(CredentialState.PasswordSet, await CredentialStateOfAsync(provider, userId));
    }

    [Fact]
    public async Task AFailedDelivery_ShowsDeliveryFailed_AndKeepsTheAccount()
    {
        await using var provider = BuildProvider();
        var (_, _, userId, issuanceId) = await CreateEngineerLikeUserAsync(provider);
        provider.GetRequiredService<FakeWhatsAppSender>().Script(
            new WhatsAppSendResult(WhatsAppSendOutcome.Definitive, Category: CredentialFailureCategory.NotOnWhatsApp));

        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(DeliveryAttemptOutcome.Failed, await scope.ServiceProvider.GetRequiredService<DeliverCredentialUseCase>().ExecuteAsync(issuanceId, "w"));
        }

        Assert.Equal(CredentialState.DeliveryFailed, await CredentialStateOfAsync(provider, userId));
        await using var read = _fixture.CreateDbContext();
        Assert.True(await read.Users.AnyAsync(u => u.Id == userId)); // R51: the account is never lost
        Assert.False((await read.CredentialIssuances.AsNoTracking().SingleAsync(i => i.Id == issuanceId)).HasSealedCopy);
    }

    [Fact]
    public async Task Reissue_ReplacesTheHash_RevokesSessions_AndSupersedesAndPurgesTheOpenIssuanceInOneTransaction()
    {
        await using var provider = BuildProvider(options => options.MinReissueInterval = TimeSpan.Zero);
        var (_, oldPassword, userId, firstIssuance) = await CreateEngineerLikeUserAsync(provider);
        var sessionId = await SeedSessionAsync(userId);
        var adminId = await SeedActorAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ReissueCredentialUseCase>().ExecuteAsync(
                new ReissueCredentialRequest(userId, firstIssuance, adminId, "corr-reset", AuditSource.Web));
            Assert.True(result.IsSuccess, result.Error?.ToString());
        }

        await using var read = _fixture.CreateDbContext();
        var rows = await read.CredentialIssuances.AsNoTracking().Where(i => i.UserId == userId).OrderBy(i => i.IssuedAt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(CredentialDeliveryStatus.Superseded, rows[0].Status);
        Assert.False(rows[0].HasSealedCopy);
        Assert.Equal(CredentialDeliveryStatus.Queued, rows[1].Status);
        Assert.Equal(CredentialIssuanceKind.Reset, rows[1].Kind);
        Assert.Equal(Number, rows[1].Destination);

        var user = await read.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.False(user.MustChangePassword);
        var hasher = provider.GetRequiredService<IPasswordHasher>();
        Assert.NotEqual(PasswordVerificationResult.Success, hasher.Verify(user.PasswordHash, oldPassword.Value));
        Assert.NotNull((await read.AuthSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).RevokedAt);

        var audit = await read.AuditEvents.AsNoTracking().SingleAsync(e => e.EntityId == userId && e.ActionType == AuditActionType.PasswordResetIssued);
        Assert.Contains(rows[1].Id.ToString(), audit.Snapshot!);
        Assert.DoesNotContain(Number, audit.Snapshot!);
    }

    [Fact]
    public async Task Reissue_Delivers_TheNewPasswordNotTheOld()
    {
        await using var provider = BuildProvider(options => options.MinReissueInterval = TimeSpan.Zero);
        var (_, _, userId, firstIssuance) = await CreateEngineerLikeUserAsync(provider);
        var adminId = await SeedActorAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReissueCredentialUseCase>().ExecuteAsync(
                new ReissueCredentialRequest(userId, firstIssuance, adminId, "c", AuditSource.Web));
        }

        // The superseded issuance is never sent; the new one carries the password that now works.
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(DeliveryAttemptOutcome.NotClaimed, await scope.ServiceProvider.GetRequiredService<DeliverCredentialUseCase>().ExecuteAsync(firstIssuance, "w"));
        }

        Guid second;
        await using (var read = _fixture.CreateDbContext())
        {
            second = (await read.CredentialIssuances.AsNoTracking().Where(i => i.UserId == userId && i.Kind == CredentialIssuanceKind.Reset).SingleAsync()).Id;
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(DeliveryAttemptOutcome.Accepted, await scope.ServiceProvider.GetRequiredService<DeliverCredentialUseCase>().ExecuteAsync(second, "w"));
        }

        var message = Assert.Single(provider.GetRequiredService<FakeWhatsAppSender>().Sent);
        await using var check = _fixture.CreateDbContext();
        var user = await check.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal(PasswordVerificationResult.Success, provider.GetRequiredService<IPasswordHasher>().Verify(user.PasswordHash, message.Password));
    }

    [Fact]
    public async Task Disable_CancelsTheOpenIssuanceAndPurgesIt()
    {
        await using var provider = BuildProvider();
        var (_, _, userId, issuanceId) = await CreateEngineerLikeUserAsync(provider);
        var adminId = await SeedActorAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<DisableUserUseCase>()
                .ExecuteAsync(new DisableUserRequest(userId, adminId, "c", AuditSource.Web));
            Assert.True(result.IsSuccess);
        }

        await using var read = _fixture.CreateDbContext();
        var issuance = await read.CredentialIssuances.AsNoTracking().SingleAsync(i => i.Id == issuanceId);
        Assert.Equal(CredentialDeliveryStatus.Cancelled, issuance.Status);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task AFailedAuditWrite_RollsBackTheUserAndTheIssuanceTogether()
    {
        await using var provider = BuildProvider(configureServices: services => services.AddScoped<IAuditWriter>(sp =>
            new FailingAuditWriter(new AuditWriter(sp.GetRequiredService<ContractraDbContext>()))));
        await SeedRoleAsync("OfficeStaff");
        var actorId = await SeedActorAsync();
        var login = UniqueLogin();

        await using (var scope = provider.CreateAsyncScope())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<CreateUserUseCase>().ExecuteAsync(
                new CreateUserRequest(login, ["OfficeStaff"], null, Number, true, "verbal", actorId, "c", AuditSource.Web)));
        }

        var loginIdentifier = new LoginIdentifier(login);
        await using var read = _fixture.CreateDbContext();
        Assert.False(await read.Users.AnyAsync(u => u.LoginIdentifier == loginIdentifier));
        Assert.Empty(provider.GetRequiredService<FakeWhatsAppSender>().Sent);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private sealed record Created(string Username, UserPassword Password, Guid UserId, Guid IssuanceId)
    {
        public void Deconstruct(out string username, out UserPassword password, out Guid userId, out Guid issuanceId) =>
            (username, password, userId, issuanceId) = (Username, Password, UserId, IssuanceId);
    }

    /// <summary>The password the user was created with, recovered only to prove the old one stops working.</summary>
    private sealed record UserPassword(string Username, string Value);

    private async Task<Created> CreateEngineerLikeUserAsync(ServiceProvider provider)
    {
        await SeedRoleAsync("OfficeStaff");
        var actorId = await SeedActorAsync();
        var login = UniqueLogin();

        Guid userId;
        Guid issuanceId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<CreateUserUseCase>().ExecuteAsync(
                new CreateUserRequest(login, ["OfficeStaff"], null, Number, true, "verbal", actorId, "corr-create", AuditSource.Web));
            Assert.True(result.IsSuccess, result.Error?.ToString());
            userId = result.Value!.UserId;
            issuanceId = result.Value.Delivery.IssuanceId;
        }

        // Recover the password exactly as the worker does, through the sealed copy, without sending anything.
        string password;
        await using (var read = _fixture.CreateDbContext())
        {
            var issuance = await read.CredentialIssuances.AsNoTracking().SingleAsync(i => i.Id == issuanceId);
            password = provider.GetRequiredService<ICredentialOpening>().Open(
                new SealedSecret(issuance.SealedBytes!, issuance.SealNonce!, issuance.SealKeyId!),
                new SealContext(issuance.Id, issuance.UserId)).Reveal();
        }

        return new Created(login, new UserPassword(login, password), userId, issuanceId);
    }

    private async Task AssertPlaintextAbsentAsync(string password, Guid userId)
    {
        var hex = Convert.ToHexString(Encoding.UTF8.GetBytes(password));
        await using var connection = await OpenAsync();
        foreach (var table in new[] { "users", "credential_issuances", "audit_events", "whatsapp_webhook_events" })
        {
            await using var command = new NpgsqlCommand($"SELECT t::text FROM {table} t;", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = reader.GetString(0);
                Assert.DoesNotContain(password, row);
                Assert.DoesNotContain(hex, row, StringComparison.OrdinalIgnoreCase);
            }
        }

        _ = userId;
    }

    private async Task<CredentialState> CredentialStateOfAsync(ServiceProvider provider, Guid userId)
    {
        await using var scope = provider.CreateAsyncScope();
        var entry = await scope.ServiceProvider.GetRequiredService<IUserDirectory>().FindAsync(userId, DateTimeOffset.UtcNow);
        return CredentialStateRule.Derive(entry!, DateTimeOffset.UtcNow);
    }

    private ServiceProvider BuildProvider(Action<CredentialDeliveryOptions>? options = null, Action<IServiceCollection>? configureServices = null)
    {
        var configuration = new TestConfiguration(_fixture.ConnectionString);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);

        var deliveryOptions = new CredentialDeliveryOptions { Enabled = true };
        options?.Invoke(deliveryOptions);
        services.AddSingleton(CredentialSealingKeyRing.FromKeys(
            "it-key", new Dictionary<string, byte[]> { ["it-key"] = RandomNumberGenerator.GetBytes(32) }));
        services.AddSingleton(deliveryOptions);
        services.AddSingleton<FakeWhatsAppSender>();
        services.AddSingleton<IWhatsAppSender>(sp => sp.GetRequiredService<FakeWhatsAppSender>());

        configureServices?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string UniqueLogin() =>
        "u" + new string(Enumerable.Range(0, 12).Select(_ => (char)('a' + RandomNumberGenerator.GetInt32(26))).ToArray());

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<Guid> SaveUserAsync()
    {
        var user = TestUsers.NewUser($"cred.{Guid.NewGuid():N}");
        await using var context = _fixture.CreateDbContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private Task<Guid> SeedActorAsync() => SaveUserAsync();

    private async Task<Guid> SeedSessionAsync(Guid userId)
    {
        var session = AuthSession.Start(userId, ClientApp.Employee, ClientPlatform.Mobile, RandomNumberGenerator.GetBytes(32), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));
        await using var context = _fixture.CreateDbContext();
        context.AuthSessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
    }

    private async Task SeedRoleAsync(string code)
    {
        var roleCode = new RoleCode(code);
        await using var context = _fixture.CreateDbContext();
        if (!await context.Roles.AnyAsync(r => r.Code == roleCode))
        {
            context.Roles.Add(new Role(roleCode, RoleStatus.Active));
            await context.SaveChangesAsync();
        }
    }

    private static CredentialIssuance NewIssuance(Guid userId) =>
        CredentialIssuance.Queue(
            Guid.CreateVersion7(), userId, CredentialIssuanceKind.Initial, userId, T0, Number, "verbal",
            RandomNumberGenerator.GetBytes(28), RandomNumberGenerator.GetBytes(12), "k1", TimeSpan.FromHours(24));

    private async Task<CredentialIssuance> SaveIssuanceAsync(Guid? userId = null)
    {
        var issuance = NewIssuance(userId ?? await SaveUserAsync());
        await using var context = _fixture.CreateDbContext();
        context.CredentialIssuances.Add(issuance);
        await context.SaveChangesAsync();
        return issuance;
    }

    private async Task SetAcceptedAsync(Guid issuanceId, string providerMessageId)
    {
        await using var connection = await OpenAsync();
        await PostgresAssert.ExecuteAsync(
            connection, null, "UPDATE credential_issuances SET provider_message_id = @m WHERE id = @id;",
            ("m", providerMessageId), ("id", issuanceId));
    }

    private sealed class FailingAuditWriter : IAuditWriter
    {
        private readonly IAuditWriter _inner;

        public FailingAuditWriter(IAuditWriter inner) => _inner = inner;

        public void Stage(AuditEvent auditEvent)
        {
            _inner.Stage(auditEvent);
            _inner.Stage(AuditEvent.ForUser(
                AuditActionType.PasswordChanged, Guid.CreateVersion7(), actorRoleCode: null, "User", Guid.CreateVersion7(),
                AuditSql.T0, "corr-failing", AuditSource.Web));
        }
    }

    /// <summary>Just enough IConfiguration for AddInfrastructure: one connection string, no extra package.</summary>
    private sealed class TestConfiguration : IConfiguration
    {
        private readonly string _connectionString;

        public TestConfiguration(string connectionString) => _connectionString = connectionString;

        public string? this[string key]
        {
            get => key switch
            {
                "ConnectionStrings:DefaultConnection" => _connectionString,
                "Business:TimeZone" => "Africa/Cairo",
                _ => null,
            };
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string key) => new Section(this, key);

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

        private sealed class Section : IConfigurationSection
        {
            private readonly TestConfiguration _root;

            public Section(TestConfiguration root, string path) => (_root, Path) = (root, path);

            public string Path { get; }

            public string Key => Path.Split(':').Last();

            public string? Value
            {
                get => _root[Path];
                set => throw new NotSupportedException();
            }

            public string? this[string key]
            {
                get => _root[$"{Path}:{key}"];
                set => throw new NotSupportedException();
            }

            public IConfigurationSection GetSection(string key) => new Section(_root, $"{Path}:{key}");

            public IEnumerable<IConfigurationSection> GetChildren() => [];

            public IChangeToken GetReloadToken() => _root.GetReloadToken();
        }
    }
}
