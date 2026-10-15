using Contractra.Domain.Identity;
using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class UserPartyLinkConfiguration : IEntityTypeConfiguration<UserPartyLink>
{
    public void Configure(EntityTypeBuilder<UserPartyLink> builder)
    {
        builder.ToTable("user_party_links");

        builder.HasKey(l => l.Id).HasName("pk_user_party_links");

        builder.Property(l => l.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(l => l.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(l => l.PartyId)
            .HasColumnName("party_id")
            .IsRequired();

        // UNIQUE(user_id) is documented (logical-data-model-ar.md §1.6).
        builder.HasIndex(l => l.UserId)
            .IsUnique()
            .HasDatabaseName("ux_user_party_links_user_id");

        // One account per Party (owner decision R30, reversing the 2026-09-26
        // decision that a party could be linked from several users).
        builder.HasIndex(l => l.PartyId)
            .IsUnique()
            .HasDatabaseName("ux_user_party_links_party_id");

        builder.HasOne<Party>()
            .WithMany()
            .HasForeignKey(l => l.PartyId)
            .HasConstraintName("fk_user_party_links_parties_party_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
