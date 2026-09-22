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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContractraDbContext).Assembly);
    }
}