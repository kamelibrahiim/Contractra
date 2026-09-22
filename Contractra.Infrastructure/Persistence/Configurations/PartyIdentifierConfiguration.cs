using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class PartyIdentifierConfiguration : IEntityTypeConfiguration<PartyIdentifier>
{
    public void Configure(EntityTypeBuilder<PartyIdentifier> builder)
    {
        builder.ToTable("party_identifiers");

        builder.HasKey(i => i.Id).HasName("pk_party_identifiers");

        builder.Property(i => i.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(i => i.PartyId)
            .HasColumnName("party_id")
            .IsRequired();

        builder.HasIndex(i => i.PartyId)
            .HasDatabaseName("ix_party_identifiers_party_id");

        // PartyIdentifierType is deliberately not a closed enum: only three
        // examples are documented (commercial registration, tax card, national ID)
        // with no statement that the list is exhaustive.
        var identifierTypeConverter = new ValueConverter<PartyIdentifierType, string>(
            v => v.Value,
            v => new PartyIdentifierType(v));

        builder.Property(i => i.IdentifierType)
            .HasColumnName("identifier_type")
            .HasConversion(identifierTypeConverter)
            .IsRequired();

        builder.Property(i => i.IdentifierValue)
            .HasColumnName("identifier_value")
            .IsRequired();

        builder.Property(i => i.JurisdictionCountry)
            .HasColumnName("jurisdiction_country");

        builder.Property(i => i.IssuingAuthority)
            .HasColumnName("issuing_authority");

        builder.Property(i => i.ValidTo)
            .HasColumnName("valid_to")
            .HasColumnType("date");

        // The current-identifier uniqueness scope
        // (identifier_type, identifier_value, jurisdiction_country, issuing_authority)
        // WHERE valid_to IS NULL, with NULLS NOT DISTINCT so that missing
        // jurisdiction_country/issuing_authority participate as a single
        // uniqueness value (BR-177), is added via raw SQL in the migration -
        // EF Core's fluent API cannot express NULLS NOT DISTINCT indexes.
    }
}
