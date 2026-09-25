using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable("user_permissions");

        builder.HasKey(up => up.Id).HasName("pk_user_permissions");

        builder.Property(up => up.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(up => up.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(up => up.PermissionId)
            .HasColumnName("permission_id")
            .IsRequired();

        builder.Property(up => up.GrantedAt)
            .HasColumnName("granted_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // The documented column name is ended_at (logical-data-model-ar.md §1.6);
        // the domain calls the same moment RevokedAt, after the Revoked lifecycle state.
        builder.Property(up => up.RevokedAt)
            .HasColumnName("ended_at")
            .HasColumnType("timestamp with time zone");

        // A permission may be granted, revoked and granted again, so there is no
        // plain UNIQUE(user_id, permission_id); only one active grant per pair.
        builder.HasIndex(up => new { up.UserId, up.PermissionId })
            .IsUnique()
            .HasFilter("ended_at IS NULL")
            .HasDatabaseName("ux_user_permissions_user_id_permission_id_active");

        builder.HasIndex(up => up.PermissionId)
            .HasDatabaseName("ix_user_permissions_permission_id");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_user_permissions_ended_at_after_granted_at",
            "ended_at IS NULL OR ended_at >= granted_at"));

        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(up => up.PermissionId)
            .HasConstraintName("fk_user_permissions_permissions_permission_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Deliberately no Payroll.View trigger here, unlike role_permissions: this
        // table is the only sanctioned path for Payroll.View (BR-235).
        // The real non-overlap guarantee is the EXCLUDE constraint
        // ex_user_permissions_no_overlapping_period, added via raw SQL in the
        // migration - EF Core's fluent API cannot express it.
    }
}
