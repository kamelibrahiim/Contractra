using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <summary>
    /// PS-04a (auth contract v1.6 §23; ADR 0007): credential_issuances (one row per password-issuing operation
    /// and its WhatsApp delivery; sealed AES-GCM copy, never plaintext; a CHECK forbids ciphertext on a finished
    /// issuance) and whatsapp_webhook_events (idempotent status events). Additive: no existing table or row
    /// changes. Down drops both tables and is for development and test databases only (plan E16). Written and
    /// tested on an isolated database; not applied to any shared database.
    /// </summary>
    public partial class AddCredentialIssuances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credential_issuances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    issued_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_category = table.Column<string>(type: "text", nullable: true),
                    destination = table.Column<string>(type: "text", nullable: false),
                    consent_affirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consent_affirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consent_basis = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_owner = table.Column<string>(type: "text", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_message_id = table.Column<string>(type: "text", nullable: true),
                    provider_status_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sealed_bytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    seal_nonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    seal_key_id = table.Column<string>(type: "text", nullable: true),
                    sealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purge_by = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credential_issuances", x => x.id);
                    table.CheckConstraint("ck_credential_issuances_attempts_not_negative", "attempts >= 0");
                    table.CheckConstraint("ck_credential_issuances_consent_basis_valid_values", "consent_basis IN ('written', 'verbal')");
                    table.CheckConstraint("ck_credential_issuances_destination_e164", "destination ~ '^\\+[1-9][0-9]{7,14}$'");
                    table.CheckConstraint("ck_credential_issuances_ended_matches_status", "(status IN ('Delivered', 'Read', 'Failed', 'Superseded', 'Cancelled')) = (ended_at IS NOT NULL)");
                    table.CheckConstraint("ck_credential_issuances_failed_has_category", "status <> 'Failed' OR failure_category IS NOT NULL");
                    table.CheckConstraint("ck_credential_issuances_failure_category_valid_values", "failure_category IS NULL OR failure_category IN ('InvalidNumber', 'NotOnWhatsApp', 'TemplateUnavailable', 'AuthOrPermission', 'RateLimited', 'ProviderOutage', 'ConsentWithdrawn', 'KeyUnavailable', 'ExpiredBeforeDelivery')");
                    table.CheckConstraint("ck_credential_issuances_kind_valid_values", "kind IN ('Initial', 'Reset')");
                    table.CheckConstraint("ck_credential_issuances_purge_by_after_issued", "purge_by > issued_at");
                    table.CheckConstraint("ck_credential_issuances_purge_recorded", "(sealed_bytes IS NOT NULL) OR (purged_at IS NOT NULL)");
                    table.CheckConstraint("ck_credential_issuances_sealed_columns_together", "(sealed_bytes IS NULL) = (seal_nonce IS NULL) AND (sealed_bytes IS NULL) = (seal_key_id IS NULL)");
                    table.CheckConstraint("ck_credential_issuances_status_valid_values", "status IN ('Queued', 'Sending', 'Accepted', 'Sent', 'Delivered', 'Read', 'Failed', 'Unknown', 'Superseded', 'Cancelled')");
                    table.CheckConstraint("ck_credential_issuances_terminal_has_no_ciphertext", "status NOT IN ('Delivered', 'Read', 'Failed', 'Superseded', 'Cancelled') OR (sealed_bytes IS NULL AND seal_nonce IS NULL AND seal_key_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_credential_issuances_users_consent_affirmed_by_user_id",
                        column: x => x.consent_affirmed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credential_issuances_users_issued_by_user_id",
                        column: x => x.issued_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credential_issuances_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_webhook_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    provider_message_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    provider_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error_code = table.Column<string>(type: "text", nullable: true),
                    error_title = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whatsapp_webhook_events", x => x.id);
                    table.CheckConstraint("ck_whatsapp_webhook_events_error_code_length", "error_code IS NULL OR char_length(error_code) <= 64");
                    table.CheckConstraint("ck_whatsapp_webhook_events_error_title_length", "error_title IS NULL OR char_length(error_title) <= 256");
                    table.CheckConstraint("ck_whatsapp_webhook_events_provider_message_id_length", "char_length(provider_message_id) BETWEEN 1 AND 256");
                    table.CheckConstraint("ck_whatsapp_webhook_events_status_valid_values", "status IN ('sent', 'delivered', 'read', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_credential_issuances_consent_affirmed_by_user_id",
                table: "credential_issuances",
                column: "consent_affirmed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_credential_issuances_due",
                table: "credential_issuances",
                column: "next_attempt_at",
                filter: "status IN ('Queued', 'Sending')");

            migrationBuilder.CreateIndex(
                name: "ix_credential_issuances_issued_by_user_id",
                table: "credential_issuances",
                column: "issued_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_credential_issuances_user_id_issued_at",
                table: "credential_issuances",
                columns: new[] { "user_id", "issued_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_credential_issuances_provider_message_id",
                table: "credential_issuances",
                column: "provider_message_id",
                unique: true,
                filter: "provider_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_whatsapp_webhook_events_message_status_timestamp",
                table: "whatsapp_webhook_events",
                columns: new[] { "provider_message_id", "status", "provider_timestamp" },
                unique: true);

            // Hand-written (EF Core cannot express it), like ex_password_reset_tickets_one_open_per_user:
            // at most one open issuance per user, checked at COMMIT, so "supersede the open issuance, queue the
            // new one" is valid in either statement order.
            migrationBuilder.Sql(
                """
                ALTER TABLE credential_issuances
                    ADD CONSTRAINT ex_credential_issuances_one_open_per_user
                    EXCLUDE USING btree (user_id WITH =)
                    WHERE (status NOT IN ('Delivered', 'Read', 'Failed', 'Superseded', 'Cancelled'))
                    DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE credential_issuances DROP CONSTRAINT IF EXISTS ex_credential_issuances_one_open_per_user;");

            migrationBuilder.DropTable(
                name: "credential_issuances");

            migrationBuilder.DropTable(
                name: "whatsapp_webhook_events");
        }
    }
}
