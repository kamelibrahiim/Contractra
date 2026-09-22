using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("parties");

        builder.HasKey(p => p.Id).HasName("pk_parties");

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        // No unique constraint on normalized_name: BR-168 explicitly removed it.
        builder.Property(p => p.NormalizedName)
            .HasColumnName("normalized_name")
            .IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(p => p.Phone)
            .HasColumnName("phone");

        builder.Property(p => p.Address)
            .HasColumnName("address");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_parties_status_valid_values",
            "status IN ('Active', 'NeedsClassification', 'Inactive')"));

        builder.HasMany(p => p.Roles)
            .WithOne()
            .HasForeignKey(r => r.PartyId)
            .HasConstraintName("fk_party_roles_parties_party_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasMany(p => p.Aliases)
            .WithOne()
            .HasForeignKey(a => a.PartyId)
            .HasConstraintName("fk_party_aliases_parties_party_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasMany(p => p.Identifiers)
            .WithOne()
            .HasForeignKey(i => i.PartyId)
            .HasConstraintName("fk_party_identifiers_parties_party_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.Metadata.FindNavigation(nameof(Party.Roles))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Party.Aliases))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Party.Identifiers))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
