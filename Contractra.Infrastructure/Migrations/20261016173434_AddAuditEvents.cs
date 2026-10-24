using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <summary>
    /// Backend Sprint 2, Slice 3 (US-BE-009): the append-only audit_events table
    /// (business rules ح-186, ح-187; auth contract v1.5 §14; repository ADR 0006
    /// §5, §7.1).
    ///
    /// Additive. Hand-added to the scaffold: statement-level triggers that reject
    /// UPDATE, DELETE and TRUNCATE with SQLSTATE 42501 (insufficient_privilege, the
    /// trigger's name as the constraint name); revocation of UPDATE, DELETE, TRUNCATE
    /// and TRIGGER from every non-owner grantee in the table's ACL, which names no
    /// role and cancels privileges granted automatically by ALTER DEFAULT PRIVILEGES;
    /// and a Down that refuses to drop a non-empty table, so a rollback cannot
    /// silently destroy audit history (plan E16: fail rather than lose). Which role
    /// the application connects as, and its SELECT/INSERT grants, are environment
    /// provisioning and deliberately not part of any migration.
    ///
    /// ck_audit_events_idempotency_key_format and _length are two CHECKs, not one:
    /// PostgreSQL's regex engine rejects a repetition bound above 255 (SQLSTATE
    /// 2201B, "invalid repetition count(s)"), so the 256-character maximum is a
    /// plain char_length comparison, and the character class uses an unbounded
    /// quantifier instead of {1,256}.
    /// </summary>
    public partial class AddAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_type = table.Column<string>(type: "text", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_role_code = table.Column<string>(type: "text", nullable: true),
                    action_type = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_document_number = table.Column<string>(type: "text", nullable: true),
                    managed_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_status = table.Column<string>(type: "text", nullable: true),
                    new_status = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    causation_id = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    schema_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_events_action_type_valid_values", "action_type IN ('UserCreated', 'UserDisabled', 'UserReactivated', 'RoleAssigned', 'RoleEnded', 'PermissionGranted', 'PermissionRevoked', 'PayrollViewGranted', 'PayrollViewRevoked', 'PasswordChanged', 'PasswordResetIssued', 'PasswordResetCompleted', 'InitialPasswordReissued', 'BootstrapPasswordReset', 'AccessDenied')");
                    table.CheckConstraint("ck_audit_events_actor_consistency", "(actor_type = 'User' AND actor_user_id IS NOT NULL) OR (actor_type = 'System' AND actor_user_id IS NULL AND actor_role_code IS NULL)");
                    table.CheckConstraint("ck_audit_events_actor_role_code_valid", "actor_role_code IS NULL OR actor_role_code ~ '^\\S(.*\\S)?$'");
                    table.CheckConstraint("ck_audit_events_actor_type_valid_values", "actor_type IN ('User', 'System')");
                    table.CheckConstraint("ck_audit_events_approved_by_not_yet_defined", "approved_by_user_id IS NULL");
                    table.CheckConstraint("ck_audit_events_business_document_number_valid", "business_document_number IS NULL OR (char_length(business_document_number) <= 128 AND business_document_number ~ '^\\S(.*\\S)?$')");
                    table.CheckConstraint("ck_audit_events_causation_id_format", "causation_id IS NULL OR causation_id ~ '^[A-Za-z0-9._-]{1,64}$'");
                    table.CheckConstraint("ck_audit_events_context_not_yet_defined", "managed_company_id IS NULL AND operation_id IS NULL AND site_id IS NULL");
                    table.CheckConstraint("ck_audit_events_correlation_id_format", "correlation_id ~ '^[A-Za-z0-9._-]{1,64}$'");
                    table.CheckConstraint("ck_audit_events_entity_type_valid", "entity_type ~ '^[A-Z][A-Za-z0-9]{0,63}$'");
                    table.CheckConstraint("ck_audit_events_idempotency_key_format", "idempotency_key IS NULL OR idempotency_key ~ '^[!-~]+$'");
                    table.CheckConstraint("ck_audit_events_idempotency_key_length", "idempotency_key IS NULL OR char_length(idempotency_key) BETWEEN 1 AND 256");
                    table.CheckConstraint("ck_audit_events_idempotency_key_prefix", "idempotency_key IS NULL OR idempotency_key LIKE action_type || ':%'");
                    table.CheckConstraint("ck_audit_events_idempotency_key_required_for_source", "source NOT IN ('OfflineSync', 'Import', 'SystemJob') OR idempotency_key IS NOT NULL");
                    table.CheckConstraint("ck_audit_events_reason_valid", "reason IS NULL OR (char_length(reason) <= 1000 AND reason ~ '^\\S(.*\\S)?$')");
                    table.CheckConstraint("ck_audit_events_schema_version_positive", "schema_version >= 1");
                    table.CheckConstraint("ck_audit_events_snapshot_valid", "snapshot IS NULL OR (jsonb_typeof(snapshot) = 'object' AND octet_length(snapshot::text) <= 8192)");
                    table.CheckConstraint("ck_audit_events_source_valid_values", "source IN ('Web', 'EmployeeApp', 'OfflineSync', 'Import', 'SystemJob')");
                    table.CheckConstraint("ck_audit_events_status_changed", "previous_status IS NULL OR previous_status <> new_status");
                    table.CheckConstraint("ck_audit_events_status_pair", "(previous_status IS NULL) = (new_status IS NULL)");
                    table.CheckConstraint("ck_audit_events_status_valid", "previous_status IS NULL OR (previous_status ~ '^\\S(.*\\S)?$' AND new_status ~ '^\\S(.*\\S)?$')");
                    table.CheckConstraint("ck_audit_events_system_actor_source", "actor_type <> 'System' OR source = 'SystemJob'");
                    table.ForeignKey(
                        name: "fk_audit_events_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audit_events_users_approved_by_user_id",
                        column: x => x.approved_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_user_id",
                table: "audit_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_approved_by_user_id",
                table: "audit_events",
                column: "approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_entity",
                table: "audit_events",
                columns: new[] { "entity_type", "entity_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_events_idempotency_key",
                table: "audit_events",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            // Revoke first, so the table never exists with a privilege it must not give.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    grantee_sql text;
                BEGIN
                    FOR grantee_sql IN
                        SELECT DISTINCT CASE WHEN acl.grantee = 0 THEN 'PUBLIC' ELSE quote_ident(pg_get_userbyid(acl.grantee)) END
                        FROM pg_class c
                        CROSS JOIN LATERAL aclexplode(c.relacl) AS acl
                        WHERE c.oid = 'audit_events'::regclass
                          AND acl.grantee <> c.relowner
                    LOOP
                        EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE, TRIGGER ON TABLE audit_events FROM %s', grantee_sql);
                    END LOOP;
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION fn_audit_append_only()
                    RETURNS trigger
                    LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION USING
                        ERRCODE = 'insufficient_privilege',
                        CONSTRAINT = TG_NAME,
                        MESSAGE = format('%s on %s is not permitted: the audit trail is append-only (BR-187).', TG_OP, TG_TABLE_NAME);
                END;
                $$;
                """);

            // FOR EACH STATEMENT: fires even when no row matches, for INSERT ... ON
            // CONFLICT DO UPDATE and MERGE, and for TRUNCATE ... CASCADE reaching the table.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_audit_events_forbid_update_delete
                    BEFORE UPDATE OR DELETE ON audit_events
                    FOR EACH STATEMENT
                    EXECUTE FUNCTION fn_audit_append_only();
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_audit_events_forbid_truncate
                    BEFORE TRUNCATE ON audit_events
                    FOR EACH STATEMENT
                    EXECUTE FUNCTION fn_audit_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Plan E16 / ADR 0006 E40: fail rather than lose audit history.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM audit_events) THEN
                        RAISE EXCEPTION USING
                            ERRCODE = 'restrict_violation',
                            MESSAGE = 'audit_events is not empty: rolling back AddAuditEvents would destroy audit history.';
                    END IF;
                END;
                $$;
                """);

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_events_forbid_truncate ON audit_events;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_events_forbid_update_delete ON audit_events;");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_audit_append_only();");
        }
    }
}
