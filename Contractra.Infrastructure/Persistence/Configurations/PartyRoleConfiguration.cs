using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class PartyRoleConfiguration : IEntityTypeConfiguration<PartyRole>
{
    public void Configure(EntityTypeBuilder<PartyRole> builder)
    {
        builder.ToTable("party_roles");

        builder.HasKey(r => r.Id).HasName("pk_party_roles");

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(r => r.PartyId)
            .HasColumnName("party_id")
            .IsRequired();

        // PartyRoleCode is deliberately not a closed enum: the documented role list
        // (mهندس/عامل/مورد/مقاول/مدين/دائن/شريك/شركة) is not confirmed exhaustive
        // (a "مندوب عهدة" custody-agent role appears in the historical import mapping
        // outside that list) - see docs/logical-data-model-ar.md §2.11.
        var roleCodeConverter = new ValueConverter<PartyRoleCode, string>(
            v => v.Value,
            v => new PartyRoleCode(v));

        builder.Property(r => r.RoleCode)
            .HasColumnName("role_code")
            .HasConversion(roleCodeConverter)
            .IsRequired();

        builder.Property(r => r.EffectiveFrom)
            .HasColumnName("effective_from")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(r => r.EffectiveTo)
            .HasColumnName("effective_to")
            .HasColumnType("date");

        builder.HasIndex(r => new { r.PartyId, r.RoleCode, r.EffectiveFrom })
            .IsUnique()
            .HasDatabaseName("ux_party_roles_party_id_role_code_effective_from");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_party_roles_effective_to_after_from",
            "effective_to IS NULL OR effective_to >= effective_from"));

        // The true non-overlap guarantee for (party_id, role_code) is an EXCLUDE
        // constraint using btree_gist + daterange, added via raw SQL in the
        // migration - EF Core's fluent API cannot express PostgreSQL EXCLUDE
        // constraints.
    }
}
