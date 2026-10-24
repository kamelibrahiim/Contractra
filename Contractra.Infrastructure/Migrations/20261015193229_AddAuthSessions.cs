using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <summary>
    /// Backend Sprint 2, Slice 2B (US-BE-008): auth_sessions (auth contract v1.5
    /// §3.3; ADR 0005 §3.1). Additive; no existing table changes.
    ///
    /// The xmin column listed below is PostgreSQL's system column, used as the
    /// optimistic-concurrency token: the Npgsql SQL generator leaves it out of
    /// the DDL (PersistenceBehaviorSpikeTests, spike 1). Down() is for
    /// development and test databases only (plan E16): it drops the table and
    /// its rows.
    /// </summary>
    public partial class AddAuthSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_app = table.Column<string>(type: "text", nullable: false),
                    client_platform = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    absolute_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoke_reason = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_sessions", x => x.id);
                    table.CheckConstraint("ck_auth_sessions_absolute_expiry_after_creation", "absolute_expires_at > created_at");
                    table.CheckConstraint("ck_auth_sessions_client_app_valid_values", "client_app IN ('admin', 'employee')");
                    table.CheckConstraint("ck_auth_sessions_client_platform_valid_values", "client_platform IN ('web', 'mobile')");
                    table.CheckConstraint("ck_auth_sessions_last_refreshed_not_before_creation", "last_refreshed_at >= created_at");
                    table.CheckConstraint("ck_auth_sessions_revocation_fields_together", "(revoked_at IS NULL) = (revoke_reason IS NULL)");
                    table.CheckConstraint("ck_auth_sessions_revoke_reason_valid_values", "revoke_reason IS NULL OR revoke_reason IN ('logout', 'idle', 'refresh_reuse', 'app_access_lost', 'user_disabled', 'password_reset_issued', 'password_reset_completed', 'initial_password_reissued', 'password_changed')");
                    table.CheckConstraint("ck_auth_sessions_revoked_not_before_creation", "revoked_at IS NULL OR revoked_at >= created_at");
                    table.ForeignKey(
                        name: "fk_auth_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_user_id_active",
                table: "auth_sessions",
                column: "user_id",
                filter: "revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_sessions");
        }
    }
}
