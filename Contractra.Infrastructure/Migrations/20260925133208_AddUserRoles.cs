using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => x.id);
                    table.CheckConstraint("ck_user_roles_ended_at_after_assigned_at", "ended_at IS NULL OR ended_at >= assigned_at");
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_roles_user_id_role_id_active",
                table: "user_roles",
                columns: new[] { "user_id", "role_id" },
                unique: true,
                filter: "ended_at IS NULL");

            // Assignments of the same role to the same user may never overlap in
            // time - including a backdated reassignment that ux_user_roles_*_active
            // cannot see. assigned_at is inclusive and ended_at exclusive ('[)'),
            // matching UserRole.IsActiveAt; a NULL ended_at is unbounded. Uses the
            // btree_gist extension installed by InitialPartySlice (same approach as
            // ex_party_roles_no_overlapping_period, docs/decisions/0001 §7).
            migrationBuilder.Sql(
                """
                ALTER TABLE user_roles
                    ADD CONSTRAINT ex_user_roles_no_overlapping_period
                    EXCLUDE USING gist (
                        user_id WITH =,
                        role_id WITH =,
                        tstzrange(assigned_at, ended_at, '[)') WITH &&
                    )
                    DEFERRABLE INITIALLY IMMEDIATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE user_roles DROP CONSTRAINT IF EXISTS ex_user_roles_no_overlapping_period;");

            // btree_gist is owned by InitialPartySlice and is not dropped here.

            migrationBuilder.DropTable(
                name: "user_roles");
        }
    }
}
