using Contractra.Domain.Identity;
using Contractra.Domain.Parties;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContractraDbContext).Assembly);
    }
}