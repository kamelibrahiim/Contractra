using System.Security.Cryptography;
using System.Text.Json;
using Contractra.Application.Audit;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Persistence;
using Contractra.Infrastructure.Persistence.Audit;
using Contractra.Infrastructure.Persistence.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// The business change and its audit event commit or roll back together (business
/// rule ح-186; auth contract v1.5 §14.2; repository ADR 0006 §6, §9): the writer only
/// stages, a failing audit row (an actor that matches no user) undoes the paired change,
/// including a bulk ExecuteUpdate run inside the caller's explicit transaction and a
/// deferred-constraint failure at commit; the writer round-trips an event exactly.
/// </summary>
public class AuditEventsAtomicityTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = AuditSql.T0;
    private const string Correlation = "corr-atomic";

    private readonly PostgresTestDatabaseFixture _fixture;

    public AuditEventsAtomicityTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- the writer ------------------------------------------------------

    [Fact]
    public async Task Writer_IsScoped_AndStagesInTheSameDbContextAsTheUseCase()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConnectionStringConfiguration(_fixture.ConnectionString));
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        await using var scope = provider.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ContractraDbContext>();
        Assert.Same(writer, scope.ServiceProvider.GetRequiredService<IAuditWriter>());

        var auditEvent = SystemEvent();
        writer.Stage(auditEvent);

        var entry = Assert.Single(dbContext.ChangeTracker.Entries<AuditEvent>());
        Assert.Same(auditEvent, entry.Entity);
        Assert.Equal(EntityState.Added, entry.State);

        await using var other = provider.CreateAsyncScope();
        Assert.NotSame(writer, other.ServiceProvider.GetRequiredService<IAuditWriter>());
        Assert.Empty(other.ServiceProvider.GetRequiredService<ContractraDbContext>().ChangeTracker.Entries<AuditEvent>());
    }

    [Fact]
    public async Task Stage_WritesNothing_AndOpensNoTransaction()
    {
        var auditEvent = SystemEvent();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            new AuditWriter(dbContext).Stage(auditEvent);

            Assert.Null(dbContext.Database.CurrentTransaction);
        }

        await using var read = _fixture.CreateDbContext();
        Assert.False(await read.AuditEvents.AnyAsync(e => e.Id == auditEvent.Id));
    }

    [Fact]
    public async Task Stage_RejectsNull()
    {
        await using var dbContext = _fixture.CreateDbContext();

        Assert.Throws<ArgumentNullException>(() => new AuditWriter(dbContext).Stage(null!));
    }

    // ---- commit together -------------------------------------------------

    [Fact]
    public async Task BusinessChangeAndAuditEvent_CommitTogether()
    {
        var userId = await SaveUserAsync();
        var disabled = AuditEvent.ForUser(
            AuditActionType.UserDisabled, userId, "Admin", "User", userId, T0, Correlation, AuditSource.Web,
            previousStatus: "Active", newStatus: "Disabled");

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            var user = await dbContext.Users.SingleAsync(u => u.Id == userId);
            user.Disable();
            new AuditWriter(dbContext).Stage(disabled);
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var read = _fixture.CreateDbContext();
        Assert.Equal(UserStatus.Disabled, (await read.Users.SingleAsync(u => u.Id == userId)).Status);
        Assert.True(await read.AuditEvents.AnyAsync(e => e.Id == disabled.Id));
    }

    // ---- roll back together ---------------------------------------------

    [Fact]
    public async Task FailingAuditRow_UndoesTheTrackedBusinessChange()
    {
        var userId = await SaveUserAsync();
        var bad = BadActorEvent();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            var user = await dbContext.Users.SingleAsync(u => u.Id == userId);
            user.Disable();
            new AuditWriter(dbContext).Stage(bad);

            await AuditSql.FailsAsync(() => dbContext.SaveChangesAsync(), "23503", "fk_audit_events_users_actor_user_id");
            // The unit of work is abandoned: no commit after a failed save (ADR 0005 E25), the context is discarded.
        }

        await using var read = _fixture.CreateDbContext();
        Assert.Equal(UserStatus.Active, (await read.Users.SingleAsync(u => u.Id == userId)).Status);
        Assert.False(await read.AuditEvents.AnyAsync(e => e.Id == bad.Id));
    }

    [Fact]
    public async Task FailingAuditRow_WithoutAnExplicitTransaction_StillSavesNothing()
    {
        var userId = await SaveUserAsync();
        var bad = BadActorEvent();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var user = await dbContext.Users.SingleAsync(u => u.Id == userId);
            user.Disable();
            new AuditWriter(dbContext).Stage(bad);

            await AuditSql.FailsAsync(() => dbContext.SaveChangesAsync(), "23503");
        }

        await using var read = _fixture.CreateDbContext();
        Assert.Equal(UserStatus.Active, (await read.Users.SingleAsync(u => u.Id == userId)).Status);
    }

    [Fact]
    public async Task FailingAuditRow_UndoesABulkRevocation_RunInsideTheCallersTransaction()
    {
        var userId = await SaveUserAsync();
        var sessionId = await SaveSessionAsync(userId);
        var bad = BadActorEvent();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            var revoked = await dbContext.AuthSessions.RevokeAllActiveAsync(userId, SessionRevokeReason.UserDisabled, T0);
            Assert.Equal(1, revoked);
            new AuditWriter(dbContext).Stage(bad);

            await AuditSql.FailsAsync(() => dbContext.SaveChangesAsync(), "23503");
        }

        await using var read = _fixture.CreateDbContext();
        Assert.Null((await read.AuthSessions.SingleAsync(s => s.Id == sessionId)).RevokedAt);
    }

    [Fact]
    public async Task WithoutATransaction_ABulkRevocationSurvivesAFailedAuditWrite_WhichIsWhyUseCasesOpenOneFirst()
    {
        // Characterization of the hazard in ADR 0006 §6: ExecuteUpdate runs and commits at
        // once when no transaction is open, so a later audit failure cannot undo it.
        var userId = await SaveUserAsync();
        var sessionId = await SaveSessionAsync(userId);
        var bad = BadActorEvent();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await dbContext.AuthSessions.RevokeAllActiveAsync(userId, SessionRevokeReason.UserDisabled, T0);
            new AuditWriter(dbContext).Stage(bad);

            await AuditSql.FailsAsync(() => dbContext.SaveChangesAsync(), "23503");
        }

        await using var read = _fixture.CreateDbContext();
        Assert.NotNull((await read.AuthSessions.SingleAsync(s => s.Id == sessionId)).RevokedAt);
        Assert.False(await read.AuditEvents.AnyAsync(e => e.Id == bad.Id));
    }

    [Fact]
    public async Task TicketIssueAndAuditEvent_CommitTogetherInTheCallersTransaction()
    {
        var userId = await SaveUserAsync();
        var ticket = NewTicket(userId);
        var issued = AuditEvent.ForUser(
            AuditActionType.PasswordResetIssued, userId, "Admin", "User", userId, T0, Correlation, AuditSource.Web,
            snapshot: new Dictionary<string, string?> { ["password_reset_ticket_id"] = ticket.Id.ToString(), ["channel"] = "operator" });

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            new AuditWriter(dbContext).Stage(issued);
            await dbContext.IssuePasswordResetTicketAsync(ticket);
            await transaction.CommitAsync();
        }

        await using var read = _fixture.CreateDbContext();
        Assert.True(await read.PasswordResetTickets.AnyAsync(t => t.Id == ticket.Id));
        Assert.True(await read.AuditEvents.AnyAsync(e => e.Id == issued.Id));
    }

    [Fact]
    public async Task FailingAuditRow_UndoesTheTicketIssue_AndKeepsTheEarlierTicketOpen()
    {
        var userId = await SaveUserAsync();
        var earlier = NewTicket(userId);
        await using (var seed = _fixture.CreateDbContext())
        {
            await seed.IssuePasswordResetTicketAsync(earlier);
        }

        var replacement = NewTicket(userId);
        var bad = BadActorEvent();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            new AuditWriter(dbContext).Stage(bad);

            await AuditSql.FailsAsync(() => dbContext.IssuePasswordResetTicketAsync(replacement), "23503");
        }

        await using var read = _fixture.CreateDbContext();
        Assert.Null((await read.PasswordResetTickets.SingleAsync(t => t.Id == earlier.Id)).SupersededAt);
        Assert.False(await read.PasswordResetTickets.AnyAsync(t => t.Id == replacement.Id));
        Assert.False(await read.AuditEvents.AnyAsync(e => e.Id == bad.Id));
    }

    [Fact]
    public async Task DeferredConstraintFailureAtCommit_RemovesTheAuditRowToo()
    {
        var userId = await SaveUserAsync();
        var first = NewTicket(userId);
        var second = NewTicket(userId);
        var audited = AuditEvent.ForUser(
            AuditActionType.PasswordResetIssued, userId, "Admin", "User", userId, T0, Correlation, AuditSource.Web);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            // Two open tickets for one user: the deferred exclusion constraint is checked at commit.
            dbContext.PasswordResetTickets.AddRange(first, second);
            new AuditWriter(dbContext).Stage(audited);
            await dbContext.SaveChangesAsync();

            await AuditSql.FailsAsync(() => transaction.CommitAsync(), "23P01", "ex_password_reset_tickets_one_open_per_user");
        }

        await using var read = _fixture.CreateDbContext();
        Assert.False(await read.AuditEvents.AnyAsync(e => e.Id == audited.Id));
        Assert.False(await read.PasswordResetTickets.AnyAsync(t => t.UserId == userId));
    }

    // ---- persistence of the event itself ---------------------------------

    [Fact]
    public async Task Event_RoundTrips_WithItsCodesAndSnapshot()
    {
        var userId = await SaveUserAsync();
        var snapshot = new Dictionary<string, string?>
        {
            ["role_code"] = "Engineer",
            ["user_role_id"] = Guid.CreateVersion7().ToString(),
            ["ended_at"] = null,
        };
        var original = AuditEvent.ForUser(
            AuditActionType.RoleAssigned, userId, "Admin", "User", userId, T0, "corr-round.trip_1", AuditSource.EmployeeApp,
            reason: "Assigned at hiring", snapshot: snapshot, idempotencyOperationKey: "op-9", idempotencySubIdentifier: "assignment-1");

        await using (var save = _fixture.CreateDbContext())
        {
            new AuditWriter(save).Stage(original);
            await save.SaveChangesAsync();
        }

        await using var read = _fixture.CreateDbContext();
        var loaded = await read.AuditEvents.AsNoTracking().SingleAsync(e => e.Id == original.Id);
        Assert.Equal(AuditActorType.User, loaded.ActorType);
        Assert.Equal(userId, loaded.ActorUserId);
        Assert.Equal("Admin", loaded.ActorRoleCode);
        Assert.Equal(AuditActionType.RoleAssigned, loaded.ActionType);
        Assert.Equal("User", loaded.EntityType);
        Assert.Equal(userId, loaded.EntityId);
        Assert.Equal(T0, loaded.OccurredAtUtc);
        Assert.Equal("Assigned at hiring", loaded.Reason);
        Assert.Equal("corr-round.trip_1", loaded.CorrelationId);
        Assert.Equal(AuditSource.EmployeeApp, loaded.Source);
        Assert.Equal("RoleAssigned:op-9:assignment-1", loaded.IdempotencyKey);
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Null(loaded.ManagedCompanyId);
        Assert.Null(loaded.ApprovedByUserId);

        using var json = JsonDocument.Parse(loaded.Snapshot!);
        Assert.Equal("Engineer", json.RootElement.GetProperty("role_code").GetString());
        Assert.Equal(snapshot["user_role_id"], json.RootElement.GetProperty("user_role_id").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("ended_at").ValueKind);

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(1, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM audit_events WHERE id = @id AND actor_type = 'User' AND action_type = 'RoleAssigned' AND source = 'EmployeeApp';",
            ("id", original.Id)));
    }

    [Fact]
    public async Task SystemEvent_RoundTrips_WithNoUserAndNoRole()
    {
        var original = AuditEvent.ForSystem(
            AuditActionType.BootstrapPasswordReset, "User", Guid.CreateVersion7(), T0, Correlation,
            idempotencyOperationKey: $"command:{Guid.NewGuid():N}", reason: "Operator reset after a lost password");

        await using (var save = _fixture.CreateDbContext())
        {
            new AuditWriter(save).Stage(original);
            await save.SaveChangesAsync();
        }

        await using var read = _fixture.CreateDbContext();
        var loaded = await read.AuditEvents.AsNoTracking().SingleAsync(e => e.Id == original.Id);
        Assert.Equal(AuditActorType.System, loaded.ActorType);
        Assert.Null(loaded.ActorUserId);
        Assert.Null(loaded.ActorRoleCode);
        Assert.Equal(AuditSource.SystemJob, loaded.Source);
        Assert.Equal(original.IdempotencyKey, loaded.IdempotencyKey);
        Assert.Null(loaded.Snapshot);
    }

    [Fact]
    public async Task ReplayWithTheSameKey_FailsTheWholeTransaction_AndCreatesNoDuplicate()
    {
        var entityId = Guid.CreateVersion7();
        var key = $"replay:{Guid.NewGuid():N}";
        var first = AuditEvent.ForSystem(AuditActionType.UserCreated, "User", entityId, T0, Correlation, key);
        var replay = AuditEvent.ForSystem(AuditActionType.UserCreated, "User", entityId, T0, Correlation, key);

        await using (var save = _fixture.CreateDbContext())
        {
            new AuditWriter(save).Stage(first);
            await save.SaveChangesAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            new AuditWriter(dbContext).Stage(replay);

            await AuditSql.FailsAsync(() => dbContext.SaveChangesAsync(), "23505", "ux_audit_events_idempotency_key");
        }

        await using var read = _fixture.CreateDbContext();
        Assert.Equal(1, await read.AuditEvents.CountAsync(e => e.IdempotencyKey == first.IdempotencyKey));
    }

    // ---- helpers ---------------------------------------------------------

    private static AuditEvent SystemEvent() =>
        AuditEvent.ForSystem(AuditActionType.UserCreated, "User", Guid.CreateVersion7(), T0, Correlation, $"op:{Guid.NewGuid():N}");

    /// <summary>A valid Domain event whose actor matches no user: the database refuses it (foreign key).</summary>
    private static AuditEvent BadActorEvent() =>
        AuditEvent.ForUser(AuditActionType.PasswordChanged, Guid.CreateVersion7(), "Admin", "User", Guid.CreateVersion7(), T0, Correlation, AuditSource.Web);

    private static PasswordResetTicket NewTicket(Guid userId) =>
        PasswordResetTicket.Issue(userId, RandomNumberGenerator.GetBytes(32), T0, T0.AddHours(24), issuedByUserId: null, "operator");

    private async Task<Guid> SaveUserAsync()
    {
        var user = TestUsers.NewUser($"Atomic.{Guid.NewGuid():N}");
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Just enough IConfiguration for AddInfrastructure: one connection string, no extra package.</summary>
    private sealed class ConnectionStringConfiguration : IConfiguration
    {
        private readonly string _connectionString;

        public ConnectionStringConfiguration(string connectionString) => _connectionString = connectionString;

        public string? this[string key]
        {
            get => key == "ConnectionStrings:DefaultConnection" ? _connectionString : null;
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string key) => new Section(this, key);

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

        private sealed class Section : IConfigurationSection
        {
            private readonly ConnectionStringConfiguration _root;

            public Section(ConnectionStringConfiguration root, string path) => (_root, Path) = (root, path);

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

    private async Task<Guid> SaveSessionAsync(Guid userId)
    {
        var session = AuthSession.Start(userId, ClientApp.Admin, ClientPlatform.Web, RandomNumberGenerator.GetBytes(32), T0, T0.AddDays(7));
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.AuthSessions.Add(session);
        await dbContext.SaveChangesAsync();
        return session.Id;
    }
}
