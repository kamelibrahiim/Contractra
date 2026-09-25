using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_permissions", x => x.id);
                    table.CheckConstraint("ck_user_permissions_ended_at_after_granted_at", "ended_at IS NULL OR ended_at >= granted_at");
                    table.ForeignKey(
                        name: "fk_user_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_permissions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_permissions_permission_id",
                table: "user_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_permissions_user_id_permission_id_active",
                table: "user_permissions",
                columns: new[] { "user_id", "permission_id" },
                unique: true,
                filter: "ended_at IS NULL");

            // Grants of the same permission to the same user may never overlap in
            // time - including a backdated regrant that the partial index cannot see.
            // granted_at is inclusive and ended_at exclusive ('[)'), matching
            // UserPermission.IsActiveAt. Same approach as
            // ex_user_roles_no_overlapping_period (docs/decisions/0003 §9.1); uses the
            // btree_gist extension installed by InitialPartySlice.
            // No Payroll.View trigger is added: unlike role_permissions, this table
            // is the only sanctioned path for Payroll.View (BR-235).
            migrationBuilder.Sql(
                """
                ALTER TABLE user_permissions
                    ADD CONSTRAINT ex_user_permissions_no_overlapping_period
                    EXCLUDE USING gist (
                        user_id WITH =,
                        permission_id WITH =,
                        tstzrange(granted_at, ended_at, '[)') WITH &&
                    )
                    DEFERRABLE INITIALLY IMMEDIATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE user_permissions DROP CONSTRAINT IF EXISTS ex_user_permissions_no_overlapping_period;");

            // btree_gist is owned by InitialPartySlice and is not dropped here.

            migrationBuilder.DropTable(
                name: "user_permissions");
        }
    }
}
