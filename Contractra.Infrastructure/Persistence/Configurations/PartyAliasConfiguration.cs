using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class PartyAliasConfiguration : IEntityTypeConfiguration<PartyAlias>
{
    public void Configure(EntityTypeBuilder<PartyAlias> builder)
    {
        builder.ToTable("party_aliases");

        builder.HasKey(a => a.Id).HasName("pk_party_aliases");

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.PartyId)
            .HasColumnName("party_id")
            .IsRequired();

        builder.Property(a => a.Alias)
            .HasColumnName("alias")
            .IsRequired();

        // Scoped to (party_id, alias) only - never global. BR-168 removed the
        // global unique constraint on alias: the same alias text may legitimately
        // belong to two different real parties.
        builder.HasIndex(a => new { a.PartyId, a.Alias })
            .IsUnique()
            .HasDatabaseName("ux_party_aliases_party_id_alias");
    }
}
