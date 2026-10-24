using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

/// <summary>
/// audit_events (Backend Sprint 2, Slice 3; auth contract v1.5 §14; repository
/// ADR 0006 §5). The enum names are the stored codes, so one catalog (the Domain
/// enum) feeds both the value conversion and the CHECK constraints and the two
/// cannot drift apart. The append-only triggers, the revocation of privileges and
/// the partial unique index are added in the migration (EF Core cannot express the
/// triggers).
/// </summary>
public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    // A value with no leading or trailing whitespace (PostgreSQL's \S is any non-space).
    private const string Trimmed = @"^\S(.*\S)?$";

    private static string TrimmedNotEmpty(string column) => $"{column} ~ '{Trimmed}'";

    private static string InList<TEnum>(string column) where TEnum : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"))})";

    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events", t =>
        {
            t.HasCheckConstraint("ck_audit_events_actor_type_valid_values", InList<AuditActorType>("actor_type"));
            t.HasCheckConstraint(
                "ck_audit_events_actor_role_code_valid",
                $"actor_role_code IS NULL OR {TrimmedNotEmpty("actor_role_code")}");
            t.HasCheckConstraint(
                "ck_audit_events_actor_consistency",
                "(actor_type = 'User' AND actor_user_id IS NOT NULL) "
                + "OR (actor_type = 'System' AND actor_user_id IS NULL AND actor_role_code IS NULL)");
            t.HasCheckConstraint("ck_audit_events_action_type_valid_values", InList<AuditActionType>("action_type"));
            t.HasCheckConstraint("ck_audit_events_entity_type_valid", "entity_type ~ '^[A-Z][A-Za-z0-9]{0,63}$'");
            t.HasCheckConstraint(
                "ck_audit_events_business_document_number_valid",
                $"business_document_number IS NULL OR (char_length(business_document_number) <= 128 AND {TrimmedNotEmpty("business_document_number")})");
            t.HasCheckConstraint(
                "ck_audit_events_context_not_yet_defined",
                "managed_company_id IS NULL AND operation_id IS NULL AND site_id IS NULL");
            t.HasCheckConstraint("ck_audit_events_status_pair", "(previous_status IS NULL) = (new_status IS NULL)");
            t.HasCheckConstraint(
                "ck_audit_events_status_changed",
                "previous_status IS NULL OR previous_status <> new_status");
            t.HasCheckConstraint(
                "ck_audit_events_status_valid",
                $"previous_status IS NULL OR ({TrimmedNotEmpty("previous_status")} AND {TrimmedNotEmpty("new_status")})");
            t.HasCheckConstraint(
                "ck_audit_events_reason_valid",
                $"reason IS NULL OR (char_length(reason) <= {AuditEvent.MaxReasonLength} AND {TrimmedNotEmpty("reason")})");
            t.HasCheckConstraint("ck_audit_events_approved_by_not_yet_defined", "approved_by_user_id IS NULL");
            t.HasCheckConstraint("ck_audit_events_correlation_id_format", "correlation_id ~ '^[A-Za-z0-9._-]{1,64}$'");
            t.HasCheckConstraint(
                "ck_audit_events_causation_id_format",
                "causation_id IS NULL OR causation_id ~ '^[A-Za-z0-9._-]{1,64}$'");
            t.HasCheckConstraint("ck_audit_events_source_valid_values", InList<AuditSource>("source"));
            t.HasCheckConstraint("ck_audit_events_system_actor_source", "actor_type <> 'System' OR source = 'SystemJob'");
            // Split in two: PostgreSQL's regex engine rejects a bound above 255
            // (SQLSTATE 2201B, "invalid repetition count(s)"), so the length is a
            // plain char_length comparison and the character class uses an
            // unbounded quantifier instead of {1,256}.
            t.HasCheckConstraint(
                "ck_audit_events_idempotency_key_length",
                $"idempotency_key IS NULL OR char_length(idempotency_key) BETWEEN 1 AND {AuditEvent.MaxIdempotencyKeyLength}");
            t.HasCheckConstraint(
                "ck_audit_events_idempotency_key_format",
                "idempotency_key IS NULL OR idempotency_key ~ '^[!-~]+$'");
            t.HasCheckConstraint(
                "ck_audit_events_idempotency_key_required_for_source",
                "source NOT IN ('OfflineSync', 'Import', 'SystemJob') OR idempotency_key IS NOT NULL");
            t.HasCheckConstraint(
                "ck_audit_events_idempotency_key_prefix",
                "idempotency_key IS NULL OR idempotency_key LIKE action_type || ':%'");
            t.HasCheckConstraint(
                "ck_audit_events_snapshot_valid",
                $"snapshot IS NULL OR (jsonb_typeof(snapshot) = 'object' AND octet_length(snapshot::text) <= {AuditEvent.MaxSnapshotBytes})");
            t.HasCheckConstraint("ck_audit_events_schema_version_positive", "schema_version >= 1");
        });

        builder.HasKey(e => e.Id).HasName("pk_audit_events");

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.ActorType).HasColumnName("actor_type").HasConversion<string>().IsRequired();
        builder.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(e => e.ActorRoleCode).HasColumnName("actor_role_code");
        builder.Property(e => e.ActionType).HasColumnName("action_type").HasConversion<string>().IsRequired();
        builder.Property(e => e.EntityType).HasColumnName("entity_type").IsRequired();
        builder.Property(e => e.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(e => e.BusinessDocumentNumber).HasColumnName("business_document_number");
        builder.Property(e => e.ManagedCompanyId).HasColumnName("managed_company_id");
        builder.Property(e => e.OperationId).HasColumnName("operation_id");
        builder.Property(e => e.SiteId).HasColumnName("site_id");
        builder.Property(e => e.PreviousStatus).HasColumnName("previous_status");
        builder.Property(e => e.NewStatus).HasColumnName("new_status");
        builder.Property(e => e.Reason).HasColumnName("reason");
        builder.Property(e => e.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").IsRequired();
        builder.Property(e => e.CausationId).HasColumnName("causation_id");
        builder.Property(e => e.Source).HasColumnName("source").HasConversion<string>().IsRequired();
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");
        builder.Property(e => e.Snapshot).HasColumnName("snapshot").HasColumnType("jsonb");
        builder.Property(e => e.SchemaVersion).HasColumnName("schema_version").IsRequired();

        // Global partial unique index (logical model §15.7; ADR 0006 E30).
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_audit_events_idempotency_key");

        builder.HasIndex(e => e.ActorUserId).HasDatabaseName("ix_audit_events_actor_user_id");
        builder.HasIndex(e => e.ApprovedByUserId).HasDatabaseName("ix_audit_events_approved_by_user_id");

        // The history of one record (addendum ق-11).
        builder.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredAtUtc })
            .HasDatabaseName("ix_audit_events_entity");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.ActorUserId)
            .HasConstraintName("fk_audit_events_users_actor_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.ApprovedByUserId)
            .HasConstraintName("fk_audit_events_users_approved_by_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
