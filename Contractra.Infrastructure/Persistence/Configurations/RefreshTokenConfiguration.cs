using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

/// <summary>
/// refresh_tokens (Backend Sprint 2, Slice 2B; auth contract v1.5 §3.1, §3.3;
/// ADR 0005 §3.2). Hash only: the token itself is never stored.
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", t =>
        {
            t.HasCheckConstraint("ck_refresh_tokens_token_hash_length", "octet_length(token_hash) = 32");
            t.HasCheckConstraint("ck_refresh_tokens_consumed_not_before_issued", "consumed_at IS NULL OR consumed_at >= issued_at");
        });

        builder.HasKey(t => t.Id).HasName("pk_refresh_tokens");

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .HasColumnName("token_hash")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(t => t.IssuedAt)
            .HasColumnName("issued_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // UPDATE ... WHERE consumed_at IS NULL: of two rotations of the same
        // token, the second matches no row and fails (ADR 0005 §4).
        builder.Property(t => t.ConsumedAt)
            .HasColumnName("consumed_at")
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        // Covers consumed tokens too: reuse detection looks them up (§3.3).
        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_refresh_tokens_token_hash");

        builder.HasIndex(t => t.SessionId)
            .HasDatabaseName("ix_refresh_tokens_session_id");

        // At most one unconsumed token per session is the deferred exclusion
        // constraint ex_refresh_tokens_one_unconsumed_per_session, added via raw
        // SQL in the migration: EF Core cannot express it, and a partial unique
        // index could not be deferred to commit.
    }
}
