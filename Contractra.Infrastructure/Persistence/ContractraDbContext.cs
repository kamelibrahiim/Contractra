using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Domain.Parties;
using Contractra.Infrastructure.Persistence.Configurations;
using Contractra.Infrastructure.Persistence.Throttling;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence;

public sealed class ContractraDbContext : DbContext
{
    public ContractraDbContext(
        DbContextOptions<ContractraDbContext> options)
        : base(options)
    {
    }

    public DbSet<Party> Parties => Set<Party>();

    public DbSet<PartyRole> PartyRoles => Set<PartyRole>();

    public DbSet<PartyAlias> PartyAliases => Set<PartyAlias>();

    public DbSet<PartyIdentifier> PartyIdentifiers => Set<PartyIdentifier>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<User> Users => Set<User>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();

    public DbSet<UserPartyLink> UserPartyLinks => Set<UserPartyLink>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PasswordResetTicket> PasswordResetTickets => Set<PasswordResetTicket>();

    public DbSet<CredentialIssuance> CredentialIssuances => Set<CredentialIssuance>();

    public DbSet<WhatsAppWebhookEvent> WhatsAppWebhookEvents => Set<WhatsAppWebhookEvent>();

    public DbSet<AuthThrottleCounter> AuthThrottleCounters => Set<AuthThrottleCounter>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContractraDbContext).Assembly);
    }
}