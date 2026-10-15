using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <summary>
    /// Backend Sprint 2, Slice 2B (US-BE-008): password_reset_tickets (auth
    /// contract v1.5 §5.6, §9.6, §10A; ADR 0005 §3.3). Additive.
    ///
    /// ex_password_reset_tickets_one_open_per_user is hand-written: at most one
    /// open (neither consumed nor superseded) ticket per user, checked at
    /// COMMIT, so "supersede the open ticket, insert the new one" is valid in
    /// either statement order. Down() is for development and test databases
    /// only (plan E16).
    /// </summary>
    public partial class AddPasswordResetTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "password_reset_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<string>(type: "text", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tickets", x => x.id);
                    table.CheckConstraint("ck_password_reset_tickets_channel_valid", "char_length(channel) BETWEEN 1 AND 32");
                    table.CheckConstraint("ck_password_reset_tickets_code_hash_length", "octet_length(code_hash) = 32");
                    table.CheckConstraint("ck_password_reset_tickets_consumed_before_expiry", "consumed_at IS NULL OR consumed_at < expires_at");
                    table.CheckConstraint("ck_password_reset_tickets_consumed_not_before_issued", "consumed_at IS NULL OR consumed_at >= issued_at");
                    table.CheckConstraint("ck_password_reset_tickets_expires_after_issued", "expires_at > issued_at");
                    table.CheckConstraint("ck_password_reset_tickets_not_consumed_and_superseded", "consumed_at IS NULL OR superseded_at IS NULL");
                    table.CheckConstraint("ck_password_reset_tickets_superseded_not_before_issued", "superseded_at IS NULL OR superseded_at >= issued_at");
                    table.ForeignKey(
                        name: "fk_password_reset_tickets_users_issued_by_user_id",
                        column: x => x.issued_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_password_reset_tickets_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tickets_issued_by_user_id",
                table: "password_reset_tickets",
                column: "issued_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tickets_user_id_issued_at",
                table: "password_reset_tickets",
                columns: new[] { "user_id", "issued_at" },
                descending: new[] { false, true });

            migrationBuilder.Sql(
                """
                ALTER TABLE password_reset_tickets
                    ADD CONSTRAINT ex_password_reset_tickets_one_open_per_user
                    EXCLUDE USING btree (user_id WITH =) WHERE (consumed_at IS NULL AND superseded_at IS NULL)
                    DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE password_reset_tickets DROP CONSTRAINT IF EXISTS ex_password_reset_tickets_one_open_per_user;");

            migrationBuilder.DropTable(
                name: "password_reset_tickets");
        }
    }
}
