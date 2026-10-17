using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Identity;
using Contractra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Slice 6 persistence pieces on PostgreSQL 18: effective permissions (contract §2), the
/// per-request lookup (§3.5), transparent rehash (§5.1, R33), refresh tokens (§3.1),
/// sessions by token hash, and AccessDenied in its own scope (§14.2, ADR 0006 §6).
/// </summary>
public class AuthSlice6PersistenceTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public AuthSlice6PersistenceTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UserAccess_IsActiveRolePermissions_UnionActiveDirectGrants()
    {
        var now = DateTimeOffset.UtcNow;
        var user = await Slice6Support.SeedUserAsync(_fixture, now, ["Engineer", "OfficeStaff"], ["Payroll.View"]);
        await using var provider = Slice6Support.BuildProvider(_fixture);
        await using var scope = provider.CreateAsyncScope();

        var access = await scope.ServiceProvider.GetRequiredService<IUserAccessReader>().GetAsync(user.Id, now);

        Assert.Equal(["Engineer", "OfficeStaff"], access.ActiveRoleCodes);
        Assert.Equal(["Apps.Admin.Access", "Apps.Employee.Access", "Payroll.View"], access.EffectivePermissions);
        Assert.Equal(["Apps.Employee.Access"], access.PermissionsByActiveRole["Engineer"]);
        Assert.Equal(["Apps.Admin.Access"], access.PermissionsByActiveRole["OfficeStaff"]);
    }

    [Fact]
    public async Task UserAccess_ExcludesEndedRolesRevokedGrantsAndFutureStarts()
    {
        var now = DateTimeOffset.UtcNow;
        var user = await Slice6Support.SeedUserAsync(_fixture, now, ["Engineer", "OfficeStaff"], ["Payroll.View"]);
        await using (var db = _fixture.CreateDbContext())
        {
            var tracked = await db.Users.Include(u => u.Roles).Include(u => u.Permissions).SingleAsync(u => u.Id == user.Id);
            tracked.EndRole(await db.Roles.SingleAsync(r => r.Code == new RoleCode("OfficeStaff")), now, now);
            tracked.RevokePermission(await db.Permissions.SingleAsync(p => p.Code == new PermissionCode("Payroll.View")), now, now);
            await db.SaveChangesAsync();
        }

        await using var provider = Slice6Support.BuildProvider(_fixture);
        await using var scope = provider.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IUserAccessReader>();

        var after = await reader.GetAsync(user.Id, now.AddSeconds(1));
        Assert.Equal(["Engineer"], after.ActiveRoleCodes);
        Assert.Equal(["Apps.Employee.Access"], after.EffectivePermissions);

        var before = await reader.GetAsync(user.Id, now.AddSeconds(-1));
        Assert.Equal(["Engineer", "OfficeStaff"], before.ActiveRoleCodes);

        var beforeAssignment = await reader.GetAsync(user.Id, now.AddDays(-2));
        Assert.Empty(beforeAssignment.ActiveRoleCodes);
        Assert.Empty(beforeAssignment.EffectivePermissions);
    }

    [Fact]
    public async Task SecurityStamp_ReadsVersionAndForcedChange_OrNullForAnUnknownUser()
    {
        await using var db = _fixture.CreateDbContext();
        var user = new User(new LoginIdentifier($"S6.{Guid.NewGuid():N}"), UserStatus.Active, TestUsers.Hash, true, DateTimeOffset.UtcNow.AddHours(1));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await using var provider = Slice6Support.BuildProvider(_fixture);
        await using var scope = provider.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ISecurityStampReader>();

        Assert.Equal(new SecurityStamp(user.SecurityVersion, true), await reader.GetAsync(user.Id));
        Assert.Null(await reader.GetAsync(Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Rehash_ReplacesOnlyTheVerifiedHash_InsideATransaction()
    {
        await using var db = _fixture.CreateDbContext();
        var user = TestUsers.NewUser($"S6.{Guid.NewGuid():N}");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await using var provider = Slice6Support.BuildProvider(_fixture);
        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPasswordRehashStore>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceHashAsync(user.Id, TestUsers.Hash, "AQAAAAIAAYagNewHash=="));

            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.BeginTransactionAsync();
            Assert.False(await store.ReplaceHashAsync(user.Id, "AQAAAAIAAYagStaleHash==", "AQAAAAIAAYagNewHash=="));
            Assert.True(await store.ReplaceHashAsync(user.Id, TestUsers.Hash, "AQAAAAIAAYagNewHash=="));
            await unitOfWork.SaveChangesAndCommitAsync();
        }

        await using var check = _fixture.CreateDbContext();
        var stored = await check.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal("AQAAAAIAAYagNewHash==", stored.PasswordHash);
        Assert.Equal(user.SecurityVersion, stored.SecurityVersion);
    }

    [Fact]
    public void RefreshTokens_Are32RandomBytesBase64Url_StoredOnlyAsTheirSha256()
    {
        var factory = new SecureRefreshTokenFactory();
        var tokens = Enumerable.Range(0, 200).Select(_ => factory.Create()).ToList();

        Assert.All(tokens, t =>
        {
            Assert.Equal(43, t.Token.Length);
            Assert.Equal(32, Base64Url.DecodeFromChars(t.Token).Length);
            Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(t.Token)), t.Hash);
            Assert.Equal(t.Hash, factory.Hash(t.Token));
        });
        Assert.Equal(200, tokens.Select(t => t.Token).Distinct().Count());
    }

    [Fact]
    public async Task SessionRepository_AddsAndFindsByTokenHash()
    {
        await using var db = _fixture.CreateDbContext();
        var user = TestUsers.NewUser($"S6.{Guid.NewGuid():N}");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var token = new SecureRefreshTokenFactory().Create();
        var now = DateTimeOffset.UtcNow;

        await using var provider = Slice6Support.BuildProvider(_fixture);
        Guid sessionId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var session = AuthSession.Start(user.Id, ClientApp.Employee, ClientPlatform.Mobile, token.Hash, now, now.AddDays(7));
            sessionId = session.Id;
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.BeginTransactionAsync();
            scope.ServiceProvider.GetRequiredService<IAuthSessionRepository>().Add(session);
            await unitOfWork.SaveChangesAndCommitAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var found = await scope.ServiceProvider.GetRequiredService<IAuthSessionRepository>().FindByRefreshTokenHashAsync(token.Hash);
            Assert.Equal(sessionId, found!.Id);
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IAuthSessionRepository>().FindByRefreshTokenHashAsync(new byte[32]));
        }
    }

    [Fact]
    public async Task AccessDenied_IsWrittenInItsOwnScope_NeverSavingTheRequestsPendingChanges()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = await Slice6Support.SeedUserAsync(_fixture, now, ["Engineer", "OfficeStaff"]);
        await using var provider = Slice6Support.BuildProvider(_fixture);

        await using var request = provider.CreateAsyncScope();
        var requestDb = request.ServiceProvider.GetRequiredService<ContractraDbContext>();
        var pending = TestUsers.NewUser($"S6.pending.{Guid.NewGuid():N}");
        requestDb.Users.Add(pending); // a pending change of "the request" that must never be saved

        await provider.GetRequiredService<IAccessDeniedAuditor>().RecordAsync(
            new AccessDeniedRecord(actor.Id, AuditSource.Web, "Apps.Admin.Access", "auth.app_access_denied", "GET /api/v1/users", "corr-access-denied-1"));

        await using var check = _fixture.CreateDbContext();
        var audit = await check.AuditEvents.AsNoTracking().SingleAsync(e => e.CorrelationId == "corr-access-denied-1");
        Assert.Equal(AuditActionType.AccessDenied, audit.ActionType);
        Assert.Equal(AuditActorType.User, audit.ActorType);
        Assert.Equal(actor.Id, audit.ActorUserId);
        Assert.Equal("OfficeStaff", audit.ActorRoleCode); // the role granting Apps.Admin.Access (§14.1)
        Assert.Equal("User", audit.EntityType);
        Assert.Equal(actor.Id, audit.EntityId);
        Assert.Equal("auth.app_access_denied: GET /api/v1/users", audit.Reason);
        Assert.Null(audit.IdempotencyKey);
        Assert.False(await check.Users.AnyAsync(u => u.Id == pending.Id));
    }

    [Fact]
    public async Task AccessDenied_WriteFailure_IsLoggedAndSwallowed()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Slice6Support.BuildProvider(_fixture, logs: logs);

        // An actor that does not exist: the FK to users refuses the insert.
        await provider.GetRequiredService<IAccessDeniedAuditor>().RecordAsync(
            new AccessDeniedRecord(Guid.CreateVersion7(), AuditSource.EmployeeApp, null, "auth.forbidden", "GET /x", "corr-access-denied-2"));

        var entry = Assert.Single(logs.Entries, e => e.Text.Contains("Audit.WriteFailed", StringComparison.Ordinal));
        Assert.Contains("corr-access-denied-2", entry.Text, StringComparison.Ordinal);
        await using var check = _fixture.CreateDbContext();
        Assert.False(await check.AuditEvents.AnyAsync(e => e.CorrelationId == "corr-access-denied-2"));
    }
}
