using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <summary>
    /// Backend Sprint 2, Slice 2B (US-BE-008): refresh_tokens (auth contract
    /// v1.5 §3.1, §3.3; ADR 0005 §3.2). Additive.
    ///
    /// ex_refresh_tokens_one_unconsumed_per_session is hand-written: at most one
    /// unconsumed token per session, checked at COMMIT (DEFERRABLE INITIALLY
    /// DEFERRED), so a rotation is valid whatever order EF Core sends its UPDATE
    /// and INSERT in. PostgreSQL cannot defer a partial unique index, hence an
    /// exclusion constraint. Down() is for development and test databases only
    /// (plan E16).
    /// </summary>
    public partial class AddRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.CheckConstraint("ck_refresh_tokens_consumed_not_before_issued", "consumed_at IS NULL OR consumed_at >= issued_at");
                    table.CheckConstraint("ck_refresh_tokens_token_hash_length", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_refresh_tokens_auth_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "auth_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_session_id",
                table: "refresh_tokens",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE refresh_tokens
                    ADD CONSTRAINT ex_refresh_tokens_one_unconsumed_per_session
                    EXCLUDE USING btree (session_id WITH =) WHERE (consumed_at IS NULL)
                    DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE refresh_tokens DROP CONSTRAINT IF EXISTS ex_refresh_tokens_one_unconsumed_per_session;");

            migrationBuilder.DropTable(
                name: "refresh_tokens");
        }
    }
}
