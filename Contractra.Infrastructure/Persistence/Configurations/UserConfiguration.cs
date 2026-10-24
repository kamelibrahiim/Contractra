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

        // Credential and security state (Backend Sprint 2, US-BE-007; auth
        // contract v1.5 §3.5, §10, §11; ADR 0003 §11). The Domain stores the hash
        // string only; "!" marks an account with no usable password.
        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .IsRequired();

        builder.Property(u => u.MustChangePassword)
            .HasColumnName("must_change_password")
            .IsRequired();

        builder.Property(u => u.TemporaryPasswordExpiresAt)
            .HasColumnName("temporary_password_expires_at")
            .HasColumnType("timestamp with time zone");

        // Also the optimistic-concurrency token: every security-state change bumps
        // it, so two writers that loaded the same version cannot both commit and
        // a change can never hide behind a version number already used.
        builder.Property(u => u.SecurityVersion)
            .HasColumnName("security_version")
            .IsRequired()
            .IsConcurrencyToken();

        // The database keeps DEFAULT false for is_bootstrap_admin (added in the
        // migration); the Domain always supplies the value, so the model declares
        // no default.
        builder.Property(u => u.IsBootstrapAdmin)
            .HasColumnName("is_bootstrap_admin")
            .IsRequired();

        builder.Ignore(u => u.HasUsablePassword);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_users_password_hash_not_empty", "password_hash <> ''");
            t.HasCheckConstraint("ck_users_security_version_positive", "security_version >= 1");
            t.HasCheckConstraint(
                "ck_users_temporary_password_requires_change",
                "temporary_password_expires_at IS NULL OR must_change_password");
            t.HasCheckConstraint(
                "ck_users_bootstrap_admin_never_must_change",
                "NOT (is_bootstrap_admin AND must_change_password)");
        });

        // At most one bootstrap admin. Changing the flag after insert is refused
        // by trg_users_forbid_bootstrap_flag_change, added via raw SQL in the
        // migration - EF Core's fluent API cannot express a trigger.
        builder.HasIndex(u => u.IsBootstrapAdmin)
            .IsUnique()
            .HasFilter("is_bootstrap_admin")
            .HasDatabaseName("ux_users_is_bootstrap_admin");

        builder.HasMany(u => u.Roles)
            .WithOne()
            .HasForeignKey(ur => ur.UserId)
            .HasConstraintName("fk_user_roles_users_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.Metadata.FindNavigation(nameof(User.Roles))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(u => u.Permissions)
            .WithOne()
            .HasForeignKey(up => up.UserId)
            .HasConstraintName("fk_user_permissions_users_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.Metadata.FindNavigation(nameof(User.Permissions))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(u => u.PartyLink)
            .WithOne()
            .HasForeignKey<UserPartyLink>(l => l.UserId)
            .HasConstraintName("fk_user_party_links_users_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
