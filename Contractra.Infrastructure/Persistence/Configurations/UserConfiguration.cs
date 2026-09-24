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

        // Stored with its supplied casing. Case-insensitive uniqueness is the
        // expression index ux_users_login_identifier_lower (lower(login_identifier)),
        // added via raw SQL in the migration - EF Core's fluent API cannot express it.
        builder.Property(u => u.LoginIdentifier)
            .HasColumnName("login_identifier")
            .HasConversion(loginIdentifierConverter)
            .IsRequired();

        builder.Property(u => u.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_users_status_valid_values",
            "status IN ('Active', 'Disabled')"));
    }
}
