using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPartySlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parties", x => x.id);
                    table.CheckConstraint("ck_parties_status_valid_values", "status IN ('Active', 'NeedsClassification', 'Inactive')");
                });

            migrationBuilder.CreateTable(
                name: "party_aliases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alias = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_aliases_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "party_identifiers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier_type = table.Column<string>(type: "text", nullable: false),
                    identifier_value = table.Column<string>(type: "text", nullable: false),
                    jurisdiction_country = table.Column<string>(type: "text", nullable: true),
                    issuing_authority = table.Column<string>(type: "text", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_identifiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_identifiers_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "party_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_code = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_roles", x => x.id);
                    table.CheckConstraint("ck_party_roles_effective_to_after_from", "effective_to IS NULL OR effective_to >= effective_from");
                    table.ForeignKey(
                        name: "fk_party_roles_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_party_aliases_party_id_alias",
                table: "party_aliases",
                columns: new[] { "party_id", "alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_party_identifiers_party_id",
                table: "party_identifiers",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ux_party_roles_party_id_role_code_effective_from",
                table: "party_roles",
                columns: new[] { "party_id", "role_code", "effective_from" },
                unique: true);

            // Requires PostgreSQL >= 15 (NULLS NOT DISTINCT) and the btree_gist
            // extension (GiST equality operator classes for uuid/text, needed by
            // the EXCLUDE constraint below). Neither is expressible through EF
            // Core's fluent API, per the approved physical design
            // (docs/decisions/ - party-identity-persistence.md).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // Layer 1 duplicate-prevention (BR-100): the same (party_id, role_code)
            // may never have overlapping effective periods. Both EffectiveFrom and
            // EffectiveTo are inclusive; a NULL EffectiveTo is unbounded, which
            // daterange's NULL upper bound already represents correctly.
            migrationBuilder.Sql(
                """
                ALTER TABLE party_roles
                    ADD CONSTRAINT ex_party_roles_no_overlapping_period
                    EXCLUDE USING gist (
                        party_id WITH =,
                        role_code WITH =,
                        daterange(effective_from, effective_to, '[]') WITH &&
                    )
                    DEFERRABLE INITIALLY IMMEDIATE;
                """);

            // BR-177: current identifiers are unique by
            // (identifier_type, identifier_value, jurisdiction_country, issuing_authority).
            // NULLS NOT DISTINCT makes a missing jurisdiction_country/issuing_authority
            // participate as a single uniqueness value, instead of PostgreSQL's default
            // of treating every NULL as distinct from every other NULL - without this,
            // two identifiers with the exact same scope and both NULL issuing_authority
            // would not collide. Closed identifiers (valid_to IS NOT NULL) do not
            // participate in this index at all.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_party_identifiers_current_scope
                    ON party_identifiers (identifier_type, identifier_value, jurisdiction_country, issuing_authority)
                    NULLS NOT DISTINCT
                    WHERE valid_to IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_party_identifiers_current_scope;");

            migrationBuilder.Sql("ALTER TABLE party_roles DROP CONSTRAINT IF EXISTS ex_party_roles_no_overlapping_period;");

            // btree_gist is intentionally not dropped here: extensions are
            // database-wide and may be relied on by other objects.

            migrationBuilder.DropTable(
                name: "party_aliases");

            migrationBuilder.DropTable(
                name: "party_identifiers");

            migrationBuilder.DropTable(
                name: "party_roles");

            migrationBuilder.DropTable(
                name: "parties");
        }
    }
}
