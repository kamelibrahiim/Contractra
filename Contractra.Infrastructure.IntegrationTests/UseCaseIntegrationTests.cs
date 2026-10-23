using System.Security.Cryptography;
using Contractra.Application.Audit;
using Contractra.Application.Identity;
using Contractra.Application.Identity.Credentials;
using Contractra.Application.Identity.UseCases;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Identity;
using Contractra.Infrastructure.Persistence;
using Contractra.Infrastructure.Persistence.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Slice 4 use cases wired through the real DI container against PostgreSQL
/// (ADR 0006 §6, §12): each use case's writes - the Domain change, the
/// session/ticket persistence helper and its audit event - commit or roll
/// back together in one transaction it opens itself, not one supplied by a
/// test double.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public class UseCaseIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public UseCaseIntegrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateUser_PersistsTheUserItsRoleAndBothAuditEvents()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var roles = await SeedRoleAsync("OfficeStaff");
        var actorId = await SeedActorUserAsync();
        var useCase = scope.ServiceProvider.GetRequiredService<CreateUserUseCase>();

        var result = await useCase.ExecuteAsync(NewCreateRequest(UniqueLogin(), actorId, "corr-1"));

        Assert.True(result.IsSuccess);

        await using var read = _fixture.CreateDbContext();
        var user = await read.Users.Include(u => u.Roles).SingleAsync(u => u.Id == result.Value!.UserId);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.False(user.MustChangePassword);
        Assert.Single(user.Roles);

        // The credential issuance is queued in the same transaction, with a sealed copy and no plaintext column.
        var issuance = await read.CredentialIssuances.SingleAsync(i => i.UserId == user.Id);
        Assert.Equal(CredentialDeliveryStatus.Queued, issuance.Status);
        Assert.Equal(CredentialIssuanceKind.Initial, issuance.Kind);
        Assert.Equal("+201000000001", issuance.Destination);
        Assert.True(issuance.HasSealedCopy);

        var auditEvents = await read.AuditEvents.Where(e => e.EntityId == user.Id).ToListAsync();
        Assert.Equal(2, auditEvents.Count);
        var createdEvent = Assert.Single(auditEvents, e => e.ActionType == AuditActionType.UserCreated);
        Assert.Contains(issuance.Id.ToString(), createdEvent.Snapshot!);
        Assert.DoesNotContain("+201000000001", createdEvent.Snapshot!);
        Assert.Contains(auditEvents, e => e.ActionType == AuditActionType.UserCreated);
        Assert.Contains(auditEvents, e => e.ActionType == AuditActionType.RoleAssigned);
    }

    [Fact]
    public async Task CreateUser_WithADuplicateLoginIdentifier_WritesNothing()
    {
        await using var provider = BuildProvider();
        await SeedRoleAsync("OfficeStaff");
        var actorId = await SeedActorUserAsync();
        var login = UniqueLogin();

        await using (var scope = provider.CreateAsyncScope())
        {
            var useCase = scope.ServiceProvider.GetRequiredService<CreateUserUseCase>();
            var first = await useCase.ExecuteAsync(NewCreateRequest(login, actorId, "corr-1"));
            Assert.True(first.IsSuccess);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var useCase = scope.ServiceProvider.GetRequiredService<CreateUserUseCase>();
            var second = await useCase.ExecuteAsync(NewCreateRequest(login, actorId, "corr-2"));

            Assert.False(second.IsSuccess);
            Assert.Equal("users.login_identifier_taken", second.Error!.Code);
        }

        var loginIdentifier = new LoginIdentifier(login);
        await using var read = _fixture.CreateDbContext();
        Assert.Equal(1, await read.Users.CountAsync(u => u.LoginIdentifier == loginIdentifier));
        var winnerId = (await read.Users.SingleAsync(u => u.LoginIdentifier == loginIdentifier)).Id;
        Assert.Equal(1, await read.CredentialIssuances.CountAsync(i => i.UserId == winnerId)); // the loser queued nothing
    }

    [Fact]
    public async Task DisableUser_RevokesSessionsAndAuditsInOneTransaction()
    {
        await using var provider = BuildProvider();
        var (userId, sessionId) = await SeedActiveUserWithSessionAsync();
        var actorId = await SeedActorUserAsync();

        await using var scope = provider.CreateAsyncScope();
        var useCase = scope.ServiceProvider.GetRequiredService<DisableUserUseCase>();

        var result = await useCase.ExecuteAsync(new DisableUserRequest(userId, actorId, "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);

        await using var read = _fixture.CreateDbContext();
        Assert.Equal(UserStatus.Disabled, (await read.Users.SingleAsync(u => u.Id == userId)).Status);
        var session = await read.AuthSessions.SingleAsync(s => s.Id == sessionId);
        Assert.NotNull(session.RevokedAt);
        Assert.Equal(SessionRevokeReason.UserDisabled, session.RevokeReason);
        Assert.True(await read.AuditEvents.AnyAsync(e => e.EntityId == userId && e.ActionType == AuditActionType.UserDisabled));
    }

    [Fact]
    public async Task DisableUser_OnTheBootstrapAdmin_ChangesNothing()
    {
        await using var provider = BuildProvider();
        var adminId = await SeedBootstrapAdminAsync();
        var actorId = await SeedActorUserAsync();

        await using var scope = provider.CreateAsyncScope();
        var useCase = scope.ServiceProvider.GetRequiredService<DisableUserUseCase>();

        var result = await useCase.ExecuteAsync(new DisableUserRequest(adminId, actorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.bootstrap_account_protected", result.Error!.Code);

        await using var read = _fixture.CreateDbContext();
        Assert.Equal(UserStatus.Active, (await read.Users.SingleAsync(u => u.Id == adminId)).Status);
        Assert.False(await read.AuditEvents.AnyAsync(e => e.EntityId == adminId && e.ActionType == AuditActionType.UserDisabled));
    }

    // ---- IssuePasswordReset ----------------------------------------------

    [Fact]
    public async Task IssuePasswordReset_CommitsEveryChangeAndItsAuditEvent_BeforeTheCodeIsDelivered()
    {
        var delivery = new RecordingDelivery();
        await using var provider = BuildProvider(services => services.AddSingleton<IPasswordResetDelivery>(delivery));
        var actorId = await SeedActorUserAsync();
        var seeded = await SeedUserWithSessionsAsync("OldPassw0rd1", sessionCount: 2);
        var earlier = await SeedOpenTicketAsync(seeded.UserId);

        bool? ticketVisibleToAnotherConnectionAtSendTime = null;
        delivery.OnSend = async _ =>
        {
            await using var other = _fixture.CreateDbContext();
            ticketVisibleToAnotherConnectionAtSendTime =
                await other.AuditEvents.AnyAsync(e => e.EntityId == seeded.UserId && e.ActionType == AuditActionType.PasswordResetIssued)
                && await other.PasswordResetTickets.CountAsync(t => t.UserId == seeded.UserId && t.SupersededAt == null && t.ConsumedAt == null) == 1;
        };

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IssuePasswordResetUseCase>().ExecuteAsync(
            new IssuePasswordResetRequest(seeded.UserId, actorId, "corr-issue", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.DeliveryFailed);
        Assert.True(ticketVisibleToAnotherConnectionAtSendTime, "The commit must be visible to other connections when the code is sent.");
        var sent = Assert.Single(delivery.Sent);

        await using var read = _fixture.CreateDbContext();
        var user = await read.Users.SingleAsync(u => u.Id == seeded.UserId);
        Assert.False(user.HasUsablePassword);
        Assert.Equal(seeded.SecurityVersion + 1, user.SecurityVersion);

        var tickets = await read.PasswordResetTickets.Where(t => t.UserId == seeded.UserId).ToListAsync();
        Assert.Equal(2, tickets.Count);
        Assert.NotNull(tickets.Single(t => t.Id == earlier.TicketId).SupersededAt);
        var open = Assert.Single(tickets, t => t.IsOpen);
        Assert.Equal(actorId, open.IssuedByUserId);
        Assert.Equal("operator", open.Channel);

        var sessions = await read.AuthSessions.Where(x => x.UserId == seeded.UserId).ToListAsync();
        Assert.All(sessions, x =>
        {
            Assert.NotNull(x.RevokedAt);
            Assert.Equal(SessionRevokeReason.PasswordResetIssued, x.RevokeReason);
        });

        var auditEvent = await read.AuditEvents.SingleAsync(e => e.EntityId == seeded.UserId);
        Assert.Equal(AuditActionType.PasswordResetIssued, auditEvent.ActionType);
        Assert.Equal(actorId, auditEvent.ActorUserId);
        Assert.Equal(AuditSource.Web, auditEvent.Source);
        Assert.Equal("corr-issue", auditEvent.CorrelationId);
        Assert.Contains(open.Id.ToString(), auditEvent.Snapshot);
        Assert.DoesNotContain(sent.Code, auditEvent.Snapshot);
    }

    [Fact]
    public async Task IssuePasswordReset_WhenTheAuditSaveFails_RollsBackTheUserTheTicketsAndTheRevocation_AndSendsNothing()
    {
        var delivery = new RecordingDelivery();
        await using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IPasswordResetDelivery>(delivery);
            UseFailingAuditWriter(services);
        });
        var actorId = await SeedActorUserAsync();
        var seeded = await SeedUserWithSessionsAsync("OldPassw0rd1", sessionCount: 2);
        var earlier = await SeedOpenTicketAsync(seeded.UserId);

        await using (var scope = provider.CreateAsyncScope())
        {
            var useCase = scope.ServiceProvider.GetRequiredService<IssuePasswordResetUseCase>();
            await AuditSql.FailsAsync(
                async () => await useCase.ExecuteAsync(new IssuePasswordResetRequest(seeded.UserId, actorId, "corr-issue", AuditSource.Web)),
                "23503");
        }

        await using var read = _fixture.CreateDbContext();
        var user = await read.Users.SingleAsync(u => u.Id == seeded.UserId);
        Assert.Equal(seeded.PasswordHash, user.PasswordHash);
        Assert.True(user.HasUsablePassword);
        Assert.Equal(seeded.SecurityVersion, user.SecurityVersion);

        // The earlier ticket is still the only one and is still open: neither the
        // supersede (a bulk UPDATE) nor the new ticket (a SaveChanges) survived.
        var ticket = await read.PasswordResetTickets.SingleAsync(t => t.UserId == seeded.UserId);
        Assert.Equal(earlier.TicketId, ticket.Id);
        Assert.Null(ticket.SupersededAt);
        Assert.True(ticket.IsOpen);

        Assert.All(await read.AuthSessions.Where(x => x.UserId == seeded.UserId).ToListAsync(), x => Assert.Null(x.RevokedAt));
        Assert.False(await read.AuditEvents.AnyAsync(e => e.EntityId == seeded.UserId));
        Assert.Empty(delivery.Sent);
    }

    // ---- CompletePasswordReset -------------------------------------------

    [Fact]
    public async Task CompletePasswordReset_PersistsThePasswordTheConsumedTicketTheRevocationAndTheAuditEventTogether()
    {
        await using var provider = BuildProvider();
        var seeded = await SeedUserWithSessionsAsync("ignored", sessionCount: 2, unusablePassword: true);
        var ticket = await SeedOpenTicketAsync(seeded.UserId, issuedBy: await SeedActorUserAsync());

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<CompletePasswordResetUseCase>().ExecuteAsync(
                new CompletePasswordResetRequest(await LoginOfAsync(seeded.UserId), ticket.Code, "BrandNewPassw0rd", "corr-complete", AuditSource.EmployeeApp));
            Assert.True(result.IsSuccess);
        }

        await using var read = _fixture.CreateDbContext();
        var user = await read.Users.SingleAsync(u => u.Id == seeded.UserId);
        var hasher = provider.GetRequiredService<IPasswordHasher>();
        Assert.True(user.HasUsablePassword);
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.Verify(user.PasswordHash, "BrandNewPassw0rd"));
        Assert.False(user.MustChangePassword);
        Assert.Equal(seeded.SecurityVersion + 1, user.SecurityVersion);

        var stored = await read.PasswordResetTickets.SingleAsync(t => t.Id == ticket.TicketId);
        Assert.NotNull(stored.ConsumedAt);

        Assert.All(await read.AuthSessions.Where(x => x.UserId == seeded.UserId).ToListAsync(), x =>
        {
            Assert.NotNull(x.RevokedAt);
            Assert.Equal(SessionRevokeReason.PasswordResetCompleted, x.RevokeReason);
        });

        var auditEvent = await read.AuditEvents.SingleAsync(e => e.EntityId == seeded.UserId);
        Assert.Equal(AuditActionType.PasswordResetCompleted, auditEvent.ActionType);
        Assert.Equal(seeded.UserId, auditEvent.ActorUserId);
        Assert.Equal(AuditSource.EmployeeApp, auditEvent.Source);
        Assert.Equal("corr-complete", auditEvent.CorrelationId);
        Assert.Contains(ticket.TicketId.ToString(), auditEvent.Snapshot);
        Assert.DoesNotContain(ticket.Code, auditEvent.Snapshot);
        Assert.DoesNotContain("BrandNewPassw0rd", auditEvent.Snapshot);
    }

    [Fact]
    public async Task CompletePasswordReset_WhenTheAuditSaveFails_ConsumesNothingAndKeepsTheOldStateAndSessions()
    {
        await using var provider = BuildProvider(UseFailingAuditWriter);
        var seeded = await SeedUserWithSessionsAsync("ignored", sessionCount: 2, unusablePassword: true);
        var ticket = await SeedOpenTicketAsync(seeded.UserId);

        await using (var scope = provider.CreateAsyncScope())
        {
            var useCase = scope.ServiceProvider.GetRequiredService<CompletePasswordResetUseCase>();
            var login = await LoginOfAsync(seeded.UserId);
            await AuditSql.FailsAsync(
                async () => await useCase.ExecuteAsync(new CompletePasswordResetRequest(login, ticket.Code, "BrandNewPassw0rd", "corr-complete", AuditSource.Web)),
                "23503");
        }

        await using var read = _fixture.CreateDbContext();
        var user = await read.Users.SingleAsync(u => u.Id == seeded.UserId);
        Assert.False(user.HasUsablePassword);
        Assert.Equal(seeded.SecurityVersion, user.SecurityVersion);
        Assert.Null((await read.PasswordResetTickets.SingleAsync(t => t.Id == ticket.TicketId)).ConsumedAt);
        Assert.All(await read.AuthSessions.Where(x => x.UserId == seeded.UserId).ToListAsync(), x => Assert.Null(x.RevokedAt));
        Assert.False(await read.AuditEvents.AnyAsync(e => e.EntityId == seeded.UserId));
    }

    [Fact]
    public async Task CompletePasswordReset_WithAWrongCode_ChangesAndAuditsNothing()
    {
        await using var provider = BuildProvider();
        var seeded = await SeedUserWithSessionsAsync("ignored", sessionCount: 1, unusablePassword: true);
        var ticket = await SeedOpenTicketAsync(seeded.UserId);
        var wrongCode = provider.GetRequiredService<IResetCodeGenerator>().Generate().Code;

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<CompletePasswordResetUseCase>().ExecuteAsync(
                new CompletePasswordResetRequest(await LoginOfAsync(seeded.UserId), wrongCode, "BrandNewPassw0rd", "corr-bad", AuditSource.Web));
            Assert.False(result.IsSuccess);
            Assert.Equal("auth.reset_code_invalid", result.Error!.Code);
        }

        await using var read = _fixture.CreateDbContext();
        Assert.Null((await read.PasswordResetTickets.SingleAsync(t => t.Id == ticket.TicketId)).ConsumedAt);
        Assert.False((await read.Users.SingleAsync(u => u.Id == seeded.UserId)).HasUsablePassword);
        Assert.False(await read.AuditEvents.AnyAsync(e => e.EntityId == seeded.UserId));
    }

    // ---- ChangePassword --------------------------------------------------

    [Fact]
    public async Task ChangePassword_PersistsThePasswordTheOtherSessionRevocationsAndTheAuditEventTogether_AndKeepsTheCurrentSession()
    {
        await using var provider = BuildProvider();
        var seeded = await SeedUserWithSessionsAsync("OldPassw0rd1", sessionCount: 3);
        var currentSessionId = seeded.SessionIds[0];

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ChangePasswordUseCase>().ExecuteAsync(
                new ChangePasswordRequest(seeded.UserId, currentSessionId, "OldPassw0rd1", "NewPassw0rd2", "corr-change", AuditSource.EmployeeApp));
            Assert.True(result.IsSuccess);
        }

        await using var read = _fixture.CreateDbContext();
        var user = await read.Users.SingleAsync(u => u.Id == seeded.UserId);
        var hasher = provider.GetRequiredService<IPasswordHasher>();
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.Verify(user.PasswordHash, "NewPassw0rd2"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.Verify(user.PasswordHash, "OldPassw0rd1"));
        Assert.Equal(seeded.SecurityVersion + 1, user.SecurityVersion);

        var sessions = await read.AuthSessions.Where(x => x.UserId == seeded.UserId).ToListAsync();
        Assert.Null(sessions.Single(x => x.Id == currentSessionId).RevokedAt);
        Assert.All(sessions.Where(x => x.Id != currentSessionId), x =>
        {
            Assert.NotNull(x.RevokedAt);
            Assert.Equal(SessionRevokeReason.PasswordChanged, x.RevokeReason);
        });

        var auditEvent = await read.AuditEvents.SingleAsync(e => e.EntityId == seeded.UserId);
        Assert.Equal(AuditActionType.PasswordChanged, auditEvent.ActionType);
        Assert.Equal(seeded.UserId, auditEvent.ActorUserId);
        Assert.Equal(AuditSource.EmployeeApp, auditEvent.Source);
        Assert.Equal("corr-change", auditEvent.CorrelationId);
        Assert.Null(auditEvent.Snapshot);
    }

    [Fact]
    public async Task ChangePassword_WhenTheAuditSaveFails_KeepsTheOldPasswordAndEverySessionActive()
    {
        await using var provider = BuildProvider(UseFailingAuditWriter);
        var seeded = await SeedUserWithSessionsAsync("OldPassw0rd1", sessionCount: 3);

        await using (var scope = provider.CreateAsyncScope())
        {
            var useCase = scope.ServiceProvider.GetRequiredService<ChangePasswordUseCase>();
            await AuditSql.FailsAsync(
                async () => await useCase.ExecuteAsync(new ChangePasswordRequest(
                    seeded.UserId, seeded.SessionIds[0], "OldPassw0rd1", "NewPassw0rd2", "corr-change", AuditSource.Web)),
                "23503");
        }

        await using var read = _fixture.CreateDbContext();
        var user = await read.Users.SingleAsync(u => u.Id == seeded.UserId);
        Assert.Equal(seeded.PasswordHash, user.PasswordHash);
        Assert.Equal(seeded.SecurityVersion, user.SecurityVersion);
        Assert.All(await read.AuthSessions.Where(x => x.UserId == seeded.UserId).ToListAsync(), x => Assert.Null(x.RevokedAt));
        Assert.False(await read.AuditEvents.AnyAsync(e => e.EntityId == seeded.UserId));
    }

    private async Task<string> LoginOfAsync(Guid userId)
    {
        await using var dbContext = _fixture.CreateDbContext();
        return (await dbContext.Users.SingleAsync(u => u.Id == userId)).LoginIdentifier.Value;
    }

    // ---- helpers -----------------------------------------------------------

    private static CreateUserRequest NewCreateRequest(string login, Guid actorId, string correlation) =>
        new(login, new[] { "OfficeStaff" }, PartyId: null, "+201000000001", ConsentAffirmed: true, "verbal", actorId, correlation, AuditSource.Web);

    /// <summary>A policy-valid unique login: letters only, so it can never look like a phone number.</summary>
    private static string UniqueLogin() =>
        "u" + new string(Enumerable.Range(0, 12).Select(_ => (char)('a' + RandomNumberGenerator.GetInt32(26))).ToArray());

    private ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConnectionStringConfiguration(_fixture.ConnectionString);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);

        // PS-04a: credential delivery on, with a throwaway sealing key and the fake sender (never a real one).
        services.AddSingleton(CredentialSealingKeyRing.FromKeys(
            "it-key", new Dictionary<string, byte[]> { ["it-key"] = RandomNumberGenerator.GetBytes(32) }));
        services.AddSingleton(new CredentialDeliveryOptions { Enabled = true });
        services.AddSingleton<FakeWhatsAppSender>();
        services.AddSingleton<IWhatsAppSender>(sp => sp.GetRequiredService<FakeWhatsAppSender>());

        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// Replaces the audit writer with one that stages the real event and then
    /// one more valid-looking event whose actor matches no user, so the
    /// database refuses the use case's final save - after any earlier save
    /// inside the same transaction has already gone through.
    /// </summary>
    private static void UseFailingAuditWriter(IServiceCollection services) =>
        services.AddScoped<IAuditWriter>(sp =>
            new FailingAuditWriter(new AuditWriter(sp.GetRequiredService<ContractraDbContext>())));

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

    /// <summary>Records sends; <see cref="OnSend"/> runs first, so a test can look at the database at send time.</summary>
    private sealed class RecordingDelivery : IPasswordResetDelivery
    {
        public string Channel => "operator";

        public List<(Guid UserId, string Code)> Sent { get; } = new();

        public Func<Guid, Task>? OnSend { get; set; }

        public Task<bool> CanReachAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public async Task SendAsync(Guid userId, string code, CancellationToken cancellationToken = default)
        {
            if (OnSend is not null)
            {
                await OnSend(userId);
            }

            Sent.Add((userId, code));
        }
    }

    private async Task<(Guid UserId, string PasswordHash, int SecurityVersion, Guid[] SessionIds)> SeedUserWithSessionsAsync(
        string password, int sessionCount, bool unusablePassword = false)
    {
        await using var provider = BuildProvider();
        var hasher = provider.GetRequiredService<IPasswordHasher>();
        var user = new User(
            new LoginIdentifier($"Atomic.{Guid.NewGuid():N}"), UserStatus.Active,
            unusablePassword ? User.UnusablePasswordHash : hasher.Hash(password), mustChangePassword: false);
        var sessions = Enumerable.Range(0, sessionCount)
            .Select(_ => AuthSession.Start(
                user.Id, ClientApp.Admin, ClientPlatform.Web, RandomNumberGenerator.GetBytes(32),
                DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddDays(7)))
            .ToArray();

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Users.Add(user);
        dbContext.AuthSessions.AddRange(sessions);
        await dbContext.SaveChangesAsync();
        return (user.Id, user.PasswordHash, user.SecurityVersion, sessions.Select(x => x.Id).ToArray());
    }

    private async Task<(Guid TicketId, string Code, byte[] Hash)> SeedOpenTicketAsync(Guid userId, Guid? issuedBy = null)
    {
        await using var provider = BuildProvider();
        var generated = provider.GetRequiredService<IResetCodeGenerator>().Generate();
        var ticket = PasswordResetTicket.Issue(
            userId, generated.Hash, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(24), issuedBy, "operator");

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.PasswordResetTickets.Add(ticket);
        await dbContext.SaveChangesAsync();
        return (ticket.Id, generated.Code, generated.Hash);
    }

    private async Task<Role> SeedRoleAsync(string code)
    {
        var roleCode = new RoleCode(code);
        await using var dbContext = _fixture.CreateDbContext();
        var existing = await dbContext.Roles.SingleOrDefaultAsync(r => r.Code == roleCode);
        if (existing is not null)
        {
            return existing;
        }

        var role = new Role(roleCode, RoleStatus.Active);
        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync();
        return role;
    }

    private async Task<Guid> SeedActorUserAsync()
    {
        var user = TestUsers.NewUser($"Actor.{Guid.NewGuid():N}");
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(Guid UserId, Guid SessionId)> SeedActiveUserWithSessionAsync()
    {
        var user = TestUsers.NewUser($"UseCase.{Guid.NewGuid():N}");
        var session = AuthSession.Start(
            user.Id, ClientApp.Admin, ClientPlatform.Web, new byte[32], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Users.Add(user);
        dbContext.AuthSessions.Add(session);
        await dbContext.SaveChangesAsync();
        return (user.Id, session.Id);
    }

    private async Task<Guid> SeedBootstrapAdminAsync()
    {
        var adminRole = await SeedRoleAsync("Admin");
        await using var dbContext = _fixture.CreateDbContext();
        var payrollViewCode = new PermissionCode(PermissionCode.PayrollView);
        var payrollView = await dbContext.Permissions.SingleOrDefaultAsync(p => p.Code == payrollViewCode);
        if (payrollView is null)
        {
            payrollView = new Permission(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active);
            dbContext.Permissions.Add(payrollView);
            await dbContext.SaveChangesAsync();
        }

        var now = DateTimeOffset.UtcNow;
        var admin = User.CreateBootstrapAdmin(
            new LoginIdentifier($"bootstrap.{Guid.NewGuid():N}"), "hash", adminRole, payrollView, now, now);
        dbContext.Users.Add(admin);
        await dbContext.SaveChangesAsync();
        return admin.Id;
    }

    /// <summary>Just enough IConfiguration for AddInfrastructure: one connection string, no extra package.</summary>
    private sealed class ConnectionStringConfiguration : IConfiguration
    {
        private readonly string _connectionString;

        public ConnectionStringConfiguration(string connectionString) => _connectionString = connectionString;

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
}
