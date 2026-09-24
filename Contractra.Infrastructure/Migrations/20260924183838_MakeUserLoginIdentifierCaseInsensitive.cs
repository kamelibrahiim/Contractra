using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeUserLoginIdentifierCaseInsensitive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Login identifiers are unique regardless of casing, while the stored
            // value keeps the casing it was given - the same approach as
            // ux_roles_code_lower / ux_permissions_code_lower
            // (docs/decisions/0003-user-identity-persistence.md). Created before the
            // case-sensitive index is dropped so uniqueness is never absent. Fails,
            // rather than merging rows, if case-only duplicates already exist.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_users_login_identifier_lower ON users (lower(login_identifier));");

            migrationBuilder.DropIndex(
                name: "ux_users_login_identifier",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_users_login_identifier",
                table: "users",
                column: "login_identifier",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_users_login_identifier_lower;");
        }
    }
}
