using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

/// <summary>
/// password_reset_tickets (Backend Sprint 2, Slice 2B; auth contract v1.5
/// §5.6, §9.6, §10A; ADR 0005 §3.3). Hash only: the code itself is never
/// stored. Tickets are kept after use or expiry (ADR 0005 E24).
/// </summary>
public sealed class PasswordResetTicketConfiguration : IEntityTypeConfiguration<PasswordResetTicket>
{
    public void Configure(EntityTypeBuilder<PasswordResetTicket> builder)
    {
        builder.ToTable("password_reset_tickets", t =>
        {
            t.HasCheckConstraint("ck_password_reset_tickets_code_hash_length", "octet_length(code_hash) = 32");
            t.HasCheckConstraint("ck_password_reset_tickets_expires_after_issued", "expires_at > issued_at");
            t.HasCheckConstraint(
                "ck_password_reset_tickets_channel_valid",
                $"char_length(channel) BETWEEN 1 AND {PasswordResetTicket.MaxChannelLength}");
            t.HasCheckConstraint("ck_password_reset_tickets_consumed_not_before_issued", "consumed_at IS NULL OR consumed_at >= issued_at");
            t.HasCheckConstraint("ck_password_reset_tickets_consumed_before_expiry", "consumed_at IS NULL OR consumed_at < expires_at");
            t.HasCheckConstraint("ck_password_reset_tickets_superseded_not_before_issued", "superseded_at IS NULL OR superseded_at >= issued_at");
            t.HasCheckConstraint("ck_password_reset_tickets_not_consumed_and_superseded", "consumed_at IS NULL OR superseded_at IS NULL");
        });

        builder.HasKey(t => t.Id).HasName("pk_password_reset_tickets");

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(t => t.CodeHash)
            .HasColumnName("code_hash")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(t => t.IssuedAt)
            .HasColumnName("issued_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(t => t.IssuedByUserId)
            .HasColumnName("issued_by_user_id");

        builder.Property(t => t.Channel)
            .HasColumnName("channel")
            .IsRequired();

        // Both closing fields are concurrency tokens: of a completion and a
        // supersession (or two completions) of the same ticket, only one commits
        // (ADR 0005 §4).
        builder.Property(t => t.ConsumedAt)
            .HasColumnName("consumed_at")
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        builder.Property(t => t.SupersededAt)
            .HasColumnName("superseded_at")
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        builder.Ignore(t => t.IsOpen);

        // The latest ticket of a user (credentialState, contract §9.2).
        builder.HasIndex(t => new { t.UserId, t.IssuedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_password_reset_tickets_user_id_issued_at");

        builder.HasIndex(t => t.IssuedByUserId)
            .HasDatabaseName("ix_password_reset_tickets_issued_by_user_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .HasConstraintName("fk_password_reset_tickets_users_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.IssuedByUserId)
            .HasConstraintName("fk_password_reset_tickets_users_issued_by_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // At most one open ticket per user is the deferred exclusion constraint
        // ex_password_reset_tickets_one_open_per_user, added via raw SQL in the
        // migration (EF Core cannot express it).
    }
}
