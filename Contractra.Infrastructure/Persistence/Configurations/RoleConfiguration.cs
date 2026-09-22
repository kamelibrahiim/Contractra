using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id).HasName("pk_roles");

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        var codeConverter = new ValueConverter<RoleCode, string>(
            v => v.Value,
            v => new RoleCode(v));

        // Stored with its supplied casing. Case-insensitive uniqueness is the
        // expression index ux_roles_code_lower (lower(code)), added via raw SQL
        // in the migration - EF Core's fluent API cannot express it.
        builder.Property(r => r.Code)
            .HasColumnName("code")
            .HasConversion(codeConverter)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_roles_status_valid_values",
            "status IN ('Active', 'Deprecated')"));

        builder.HasMany(r => r.Permissions)
            .WithOne()
            .HasForeignKey(rp => rp.RoleId)
            .HasConstraintName("fk_role_permissions_roles_role_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.Metadata.FindNavigation(nameof(Role.Permissions))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
