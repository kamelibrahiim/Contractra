using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(rp => rp.Id).HasName("pk_role_permissions");

        builder.Property(rp => rp.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(rp => rp.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(rp => rp.PermissionId)
            .HasColumnName("permission_id")
            .IsRequired();

        // The leading role_id column of this index also serves the role FK.
        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionId })
            .IsUnique()
            .HasDatabaseName("ux_role_permissions_role_id_permission_id");

        builder.HasIndex(rp => rp.PermissionId)
            .HasDatabaseName("ix_role_permissions_permission_id");

        // The Payroll.View guard (BR-235) cannot be a CHECK constraint because it
        // depends on permissions.code; it is a trigger added via raw SQL in the
        // migration (trg_role_permissions_forbid_payroll_view).
    }
}
