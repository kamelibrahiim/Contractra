using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <summary>
    /// Backend Sprint 2, Slice 2B (US-BE-008): auth_throttle_counters, the
    /// shared store for the persisted brute-force throttle layers (auth contract
    /// v1.5 §17; ADR 0005 §3.4). Additive; the five columns the contract allows.
    /// Expired rows are deleted by AuthThrottleStore.DeleteExpiredAsync; nothing
    /// else in Slice 2B deletes data (ADR 0005 E24). Down() is for development
    /// and test databases only (plan E16).
    /// </summary>
    public partial class AddAuthThrottleCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_throttle_counters",
                columns: table => new
                {
                    scope = table.Column<string>(type: "text", nullable: false),
                    key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    blocked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_throttle_counters", x => new { x.scope, x.key_hash });
                    table.CheckConstraint("ck_auth_throttle_counters_blocked_until_not_before_window_start", "blocked_until IS NULL OR blocked_until >= window_start");
                    table.CheckConstraint("ck_auth_throttle_counters_failure_count_positive", "failure_count >= 1");
                    table.CheckConstraint("ck_auth_throttle_counters_key_hash_length", "octet_length(key_hash) = 32");
                    table.CheckConstraint("ck_auth_throttle_counters_scope_valid_values", "scope IN ('login_pair', 'identifier', 'change_password_user')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_auth_throttle_counters_window_start",
                table: "auth_throttle_counters",
                column: "window_start");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_throttle_counters");
        }
    }
}
