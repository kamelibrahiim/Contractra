using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");

        builder.HasKey(ur => ur.Id).HasName("pk_user_roles");

        builder.Property(ur => ur.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(ur => ur.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(ur => ur.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(ur => ur.AssignedAt)
            .HasColumnName("assigned_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(ur => ur.EndedAt)
            .HasColumnName("ended_at")
            .HasColumnType("timestamp with time zone");

        // A role may be assigned, ended and assigned again, so there is no plain
        // UNIQUE(user_id, role_id); only one open assignment per (user, role).
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId })
            .IsUnique()
            .HasFilter("ended_at IS NULL")
            .HasDatabaseName("ux_user_roles_user_id_role_id_active");

        builder.HasIndex(ur => ur.RoleId)
            .HasDatabaseName("ix_user_roles_role_id");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_user_roles_ended_at_after_assigned_at",
            "ended_at IS NULL OR ended_at >= assigned_at"));

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .HasConstraintName("fk_user_roles_roles_role_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // The partial index cannot stop a backdated reassignment from overlapping
        // an ended one. The real non-overlap guarantee is the EXCLUDE constraint
        // ex_user_roles_no_overlapping_period (btree_gist + tstzrange), added via
        // raw SQL in the migration - EF Core's fluent API cannot express it.
    }
}
