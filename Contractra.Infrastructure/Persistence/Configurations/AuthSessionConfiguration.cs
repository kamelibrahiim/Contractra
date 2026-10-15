using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

/// <summary>
/// auth_sessions (Backend Sprint 2, Slice 2B; auth contract v1.5 §3.3;
/// ADR 0005 §3.1).
/// </summary>
public sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    /// <summary>
    /// Shadow optimistic-concurrency token mapped to PostgreSQL's xmin system
    /// column (no real column; verified by PersistenceBehaviorSpikeTests). Any
    /// update of the row - a rotation, a revocation, a bulk revoke - changes it,
    /// so a rotation that read the session before a revocation cannot commit.
    /// </summary>
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions", t =>
        {
            t.HasCheckConstraint("ck_auth_sessions_client_app_valid_values", AuthCodes.InList("client_app", AuthCodes.ClientApps));
            t.HasCheckConstraint("ck_auth_sessions_client_platform_valid_values", AuthCodes.InList("client_platform", AuthCodes.ClientPlatforms));
            t.HasCheckConstraint("ck_auth_sessions_absolute_expiry_after_creation", "absolute_expires_at > created_at");
            t.HasCheckConstraint("ck_auth_sessions_last_refreshed_not_before_creation", "last_refreshed_at >= created_at");
            t.HasCheckConstraint("ck_auth_sessions_revocation_fields_together", "(revoked_at IS NULL) = (revoke_reason IS NULL)");
            t.HasCheckConstraint("ck_auth_sessions_revoked_not_before_creation", "revoked_at IS NULL OR revoked_at >= created_at");
            t.HasCheckConstraint(
                "ck_auth_sessions_revoke_reason_valid_values",
                $"revoke_reason IS NULL OR {AuthCodes.InList("revoke_reason", AuthCodes.RevokeReasons)}");
        });

        builder.HasKey(s => s.Id).HasName("pk_auth_sessions");

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(s => s.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(s => s.ClientApp)
            .HasColumnName("client_app")
            .HasConversion(AuthCodes.Converter(AuthCodes.ClientApps))
            .IsRequired();

        builder.Property(s => s.ClientPlatform)
            .HasColumnName("client_platform")
            .HasConversion(AuthCodes.Converter(AuthCodes.ClientPlatforms))
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.AbsoluteExpiresAt)
            .HasColumnName("absolute_expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.LastRefreshedAt)
            .HasColumnName("last_refreshed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(s => s.RevokeReason)
            .HasColumnName("revoke_reason")
            .HasConversion(AuthCodes.Converter(AuthCodes.RevokeReasons));

        builder.Property<uint>(RowVersion).IsRowVersion();

        // Revoking every session of a user touches only the active ones.
        builder.HasIndex(s => s.UserId)
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_auth_sessions_user_id_active");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .HasConstraintName("fk_auth_sessions_users_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasMany(s => s.RefreshTokens)
            .WithOne()
            .HasForeignKey(t => t.SessionId)
            .HasConstraintName("fk_refresh_tokens_auth_sessions_session_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.Metadata.FindNavigation(nameof(AuthSession.RefreshTokens))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
