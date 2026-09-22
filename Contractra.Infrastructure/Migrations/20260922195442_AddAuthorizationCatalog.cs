using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                    table.CheckConstraint("ck_permissions_status_valid_values", "status IN ('Active', 'Deprecated')");
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.CheckConstraint("ck_roles_status_valid_values", "status IN ('Active', 'Deprecated')");
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ux_role_permissions_role_id_permission_id",
                table: "role_permissions",
                columns: new[] { "role_id", "permission_id" },
                unique: true);

            // Role and permission codes are identifiers: unique regardless of
            // casing, while the stored value keeps the casing it was given.
            // Expression indexes are not expressible through EF Core's fluent API
            // (docs/decisions/0002-authorization-catalog-persistence.md).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_roles_code_lower ON roles (lower(code));");

            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_permissions_code_lower ON permissions (lower(code));");

            // BR-235 / ح-235 / FR-AUTH-006: Payroll.View is never granted through a
            // role. A CHECK constraint cannot read permissions.code, so this is a
            // trigger. The permission row is read FOR SHARE so that a concurrent
            // rename of the same permission (guarded below) waits for this
            // transaction instead of racing it.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION fn_role_permissions_forbid_payroll_view()
                    RETURNS trigger
                    LANGUAGE plpgsql
                AS $$
                DECLARE
                    granted_code text;
                BEGIN
                    SELECT code INTO granted_code
                    FROM permissions
                    WHERE id = NEW.permission_id
                    FOR SHARE;

                    IF lower(granted_code) = 'payroll.view' THEN
                        RAISE EXCEPTION USING
                            ERRCODE = 'check_violation',
                            CONSTRAINT = 'trg_role_permissions_forbid_payroll_view',
                            MESSAGE = 'Payroll.View cannot be granted through a role (BR-235).';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_role_permissions_forbid_payroll_view
                    BEFORE INSERT OR UPDATE ON role_permissions
                    FOR EACH ROW
                    EXECUTE FUNCTION fn_role_permissions_forbid_payroll_view();
                """);

            // Closes the direct-SQL bypass of the rule above: a permission that is
            // already granted to a role cannot be renamed to Payroll.View. This is
            // deliberately not a general code-immutability rule - only renames to
            // Payroll.View on a granted permission are rejected.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION fn_permissions_forbid_granted_rename_to_payroll_view()
                    RETURNS trigger
                    LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM role_permissions WHERE permission_id = NEW.id) THEN
                        RAISE EXCEPTION USING
                            ERRCODE = 'check_violation',
                            CONSTRAINT = 'trg_permissions_forbid_granted_rename_to_payroll_view',
                            MESSAGE = 'A permission granted to a role cannot be renamed to Payroll.View (BR-235).';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_permissions_forbid_granted_rename_to_payroll_view
                    BEFORE UPDATE OF code ON permissions
                    FOR EACH ROW
                    WHEN (lower(NEW.code) = 'payroll.view')
                    EXECUTE FUNCTION fn_permissions_forbid_granted_rename_to_payroll_view();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_permissions_forbid_granted_rename_to_payroll_view ON permissions;");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_role_permissions_forbid_payroll_view ON role_permissions;");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_permissions_forbid_granted_rename_to_payroll_view();");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_role_permissions_forbid_payroll_view();");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_permissions_code_lower;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_roles_code_lower;");

            // Nothing owned by InitialPartySlice (e.g. the btree_gist extension)
            // is touched here.

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}
