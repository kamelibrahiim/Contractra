using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <summary>
    /// Owner decision R30: one account per Party. Reverses the 2026-09-26 decision
    /// that a party could be linked from several users (ADR 0003 §9.3, §11).
    ///
    /// The unique index is created before the old one is dropped, so party_id is
    /// never unindexed. If a party is already linked from more than one user,
    /// creating the index fails (23505) and the migration rolls back without
    /// touching any row; resolving such rows is a decision made before the
    /// migration, using the pre-check query in ADR 0003 §11.
    /// </summary>
    public partial class MakeUserPartyLinkOnePerParty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_user_party_links_party_id",
                table: "user_party_links",
                column: "party_id",
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_user_party_links_party_id",
                table: "user_party_links");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_user_party_links_party_id",
                table: "user_party_links",
                column: "party_id");

            migrationBuilder.DropIndex(
                name: "ux_user_party_links_party_id",
                table: "user_party_links");
        }
    }
}
