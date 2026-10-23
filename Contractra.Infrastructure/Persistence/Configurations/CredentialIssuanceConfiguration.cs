using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

/// <summary>
/// credential_issuances (PS-04a; ADR 0007 §3, §6; auth contract v1.6 §23). One row per password-issuing
/// operation and its WhatsApp delivery. There is no plaintext column and no copy of the password hash;
/// the sealed columns hold only AES-GCM ciphertext and are null once the issuance ends (CHECK).
/// </summary>
public sealed class CredentialIssuanceConfiguration : IEntityTypeConfiguration<CredentialIssuance>
{
    private const string TerminalStatuses = "'Delivered', 'Read', 'Failed', 'Superseded', 'Cancelled'";

    public void Configure(EntityTypeBuilder<CredentialIssuance> builder)
    {
        builder.ToTable("credential_issuances", t =>
        {
            t.HasCheckConstraint("ck_credential_issuances_kind_valid_values", "kind IN ('Initial', 'Reset')");
            t.HasCheckConstraint(
                "ck_credential_issuances_status_valid_values",
                "status IN ('Queued', 'Sending', 'Accepted', 'Sent', 'Delivered', 'Read', 'Failed', 'Unknown', 'Superseded', 'Cancelled')");
            t.HasCheckConstraint(
                "ck_credential_issuances_failure_category_valid_values",
                "failure_category IS NULL OR failure_category IN ('InvalidNumber', 'NotOnWhatsApp', 'TemplateUnavailable', 'AuthOrPermission', 'RateLimited', 'ProviderOutage', 'ConsentWithdrawn', 'KeyUnavailable', 'ExpiredBeforeDelivery')");
            t.HasCheckConstraint("ck_credential_issuances_failed_has_category", "status <> 'Failed' OR failure_category IS NOT NULL");
            t.HasCheckConstraint("ck_credential_issuances_consent_basis_valid_values", "consent_basis IN ('written', 'verbal')");
            t.HasCheckConstraint("ck_credential_issuances_destination_e164", "destination ~ '^\\+[1-9][0-9]{7,14}$'");
            t.HasCheckConstraint("ck_credential_issuances_attempts_not_negative", "attempts >= 0");
            t.HasCheckConstraint("ck_credential_issuances_purge_by_after_issued", "purge_by > issued_at");
            t.HasCheckConstraint("ck_credential_issuances_ended_matches_status", $"(status IN ({TerminalStatuses})) = (ended_at IS NOT NULL)");

            // The sealed copy is all-present or all-absent (nonce, key id, sealed-at move with the bytes)...
            t.HasCheckConstraint(
                "ck_credential_issuances_sealed_columns_together",
                "(sealed_bytes IS NULL) = (seal_nonce IS NULL) AND (sealed_bytes IS NULL) = (seal_key_id IS NULL)");

            // ...and a finished issuance never keeps ciphertext (ADR 0007 §6).
            t.HasCheckConstraint(
                "ck_credential_issuances_terminal_has_no_ciphertext",
                $"status NOT IN ({TerminalStatuses}) OR (sealed_bytes IS NULL AND seal_nonce IS NULL AND seal_key_id IS NULL)");

            t.HasCheckConstraint(
                "ck_credential_issuances_purge_recorded",
                "(sealed_bytes IS NOT NULL) OR (purged_at IS NOT NULL)");
        });

        builder.HasKey(i => i.Id).HasName("pk_credential_issuances");

        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(i => i.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(i => i.Kind).HasColumnName("kind").HasConversion<string>().IsRequired();
        builder.Property(i => i.IssuedByUserId).HasColumnName("issued_by_user_id").IsRequired();
        builder.Property(i => i.IssuedAt).HasColumnName("issued_at").HasColumnType("timestamp with time zone").IsRequired();

        // Status and Attempts are concurrency tokens: of a worker claim, a webhook update and a sweep of one
        // issuance, only one commits from a given state.
        builder.Property(i => i.Status).HasColumnName("status").HasConversion<string>().IsRequired().IsConcurrencyToken();
        builder.Property(i => i.Attempts).HasColumnName("attempts").IsRequired().IsConcurrencyToken();

        builder.Property(i => i.EndedAt).HasColumnName("ended_at").HasColumnType("timestamp with time zone");
        builder.Property(i => i.FailureCategory).HasColumnName("failure_category").HasConversion<string>();
        builder.Property(i => i.Destination).HasColumnName("destination").IsRequired();
        builder.Property(i => i.ConsentAffirmedByUserId).HasColumnName("consent_affirmed_by_user_id").IsRequired();
        builder.Property(i => i.ConsentAffirmedAt).HasColumnName("consent_affirmed_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(i => i.ConsentBasis).HasColumnName("consent_basis").IsRequired();
        builder.Property(i => i.NextAttemptAt).HasColumnName("next_attempt_at").HasColumnType("timestamp with time zone");
        builder.Property(i => i.LeaseOwner).HasColumnName("lease_owner");
        builder.Property(i => i.LeaseUntil).HasColumnName("lease_until").HasColumnType("timestamp with time zone");
        builder.Property(i => i.ProviderMessageId).HasColumnName("provider_message_id");
        builder.Property(i => i.ProviderStatusAt).HasColumnName("provider_status_at").HasColumnType("timestamp with time zone");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.Property(i => i.SealedBytes).HasColumnName("sealed_bytes").HasColumnType("bytea");
        builder.Property(i => i.SealNonce).HasColumnName("seal_nonce").HasColumnType("bytea");
        builder.Property(i => i.SealKeyId).HasColumnName("seal_key_id");
        builder.Property(i => i.SealedAt).HasColumnName("sealed_at").HasColumnType("timestamp with time zone");
        builder.Property(i => i.PurgeBy).HasColumnName("purge_by").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(i => i.PurgedAt).HasColumnName("purged_at").HasColumnType("timestamp with time zone");

        builder.Ignore(i => i.IsOpen);
        builder.Ignore(i => i.HasSealedCopy);

        builder.HasIndex(i => new { i.UserId, i.IssuedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_credential_issuances_user_id_issued_at");

        builder.HasIndex(i => i.ProviderMessageId)
            .IsUnique()
            .HasFilter("provider_message_id IS NOT NULL")
            .HasDatabaseName("ux_credential_issuances_provider_message_id");

        builder.HasIndex(i => i.NextAttemptAt)
            .HasFilter("status IN ('Queued', 'Sending')")
            .HasDatabaseName("ix_credential_issuances_due");

        builder.HasIndex(i => i.IssuedByUserId).HasDatabaseName("ix_credential_issuances_issued_by_user_id");
        builder.HasIndex(i => i.ConsentAffirmedByUserId).HasDatabaseName("ix_credential_issuances_consent_affirmed_by_user_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .HasConstraintName("fk_credential_issuances_users_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.IssuedByUserId)
            .HasConstraintName("fk_credential_issuances_users_issued_by_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.ConsentAffirmedByUserId)
            .HasConstraintName("fk_credential_issuances_users_consent_affirmed_by_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // At most one open issuance per user is the deferred exclusion constraint
        // ex_credential_issuances_one_open_per_user, added via raw SQL in the migration.
    }
}

/// <summary>A processed WhatsApp status event (contract §23.6): id, status, provider timestamp and error code/title only.</summary>
public sealed class WhatsAppWebhookEvent
{
    public long Id { get; set; }

    public string ProviderMessageId { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTimeOffset ProviderTimestamp { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorTitle { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class WhatsAppWebhookEventConfiguration : IEntityTypeConfiguration<WhatsAppWebhookEvent>
{
    public void Configure(EntityTypeBuilder<WhatsAppWebhookEvent> builder)
    {
        builder.ToTable("whatsapp_webhook_events", t =>
        {
            t.HasCheckConstraint("ck_whatsapp_webhook_events_status_valid_values", "status IN ('sent', 'delivered', 'read', 'failed')");
            t.HasCheckConstraint("ck_whatsapp_webhook_events_provider_message_id_length", "char_length(provider_message_id) BETWEEN 1 AND 256");
            t.HasCheckConstraint("ck_whatsapp_webhook_events_error_code_length", "error_code IS NULL OR char_length(error_code) <= 64");
            t.HasCheckConstraint("ck_whatsapp_webhook_events_error_title_length", "error_title IS NULL OR char_length(error_title) <= 256");
        });

        builder.HasKey(e => e.Id).HasName("pk_whatsapp_webhook_events");
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(e => e.ProviderMessageId).HasColumnName("provider_message_id").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").IsRequired();
        builder.Property(e => e.ProviderTimestamp).HasColumnName("provider_timestamp").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(e => e.ErrorCode).HasColumnName("error_code");
        builder.Property(e => e.ErrorTitle).HasColumnName("error_title");
        builder.Property(e => e.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(e => new { e.ProviderMessageId, e.Status, e.ProviderTimestamp })
            .IsUnique()
            .HasDatabaseName("ux_whatsapp_webhook_events_message_status_timestamp");
    }
}
