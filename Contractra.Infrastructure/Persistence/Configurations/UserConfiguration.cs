using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id).HasName("pk_users");

        builder.Property(u => u.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        var loginIdentifierConverter = new ValueConverter<LoginIdentifier, string>(
            v => v.Value,
            v => new LoginIdentifier(v));

        // Login identifiers are stored exactly as supplied.
        // Uniqueness is intentionally case-sensitive; no normalization is applied.
        builder.Property(u => u.LoginIdentifier)
            .HasColumnName("login_identifier")
            .HasConversion(loginIdentifierConverter)
            .IsRequired();

        builder.HasIndex(u => u.LoginIdentifier)
            .IsUnique()
            .HasDatabaseName("ux_users_login_identifier");

        builder.Property(u => u.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_users_status_valid_values",
            "status IN ('Active', 'Disabled')"));
    }
}
