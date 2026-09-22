using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(p => p.Id).HasName("pk_permissions");

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        var codeConverter = new ValueConverter<PermissionCode, string>(
            v => v.Value,
            v => new PermissionCode(v));

        // Stored with its supplied casing. Case-insensitive uniqueness is the
        // expression index ux_permissions_code_lower (lower(code)), and the
        // Payroll.View rename guard is a trigger - both added via raw SQL in
        // the migration.
        builder.Property(p => p.Code)
            .HasColumnName("code")
            .HasConversion(codeConverter)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_permissions_status_valid_values",
            "status IN ('Active', 'Deprecated')"));

        builder.HasMany<RolePermission>()
            .WithOne()
            .HasForeignKey(rp => rp.PermissionId)
            .HasConstraintName("fk_role_permissions_permissions_permission_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
