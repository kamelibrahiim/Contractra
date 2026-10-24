using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <summary>
    /// Backend Sprint 2, Slice 2 (US-BE-007): credential and security state on
    /// users (auth contract v1.5 §3.5, §10, §11; ADR 0003 §11).
    ///
    /// Additive and data-preserving (plan E16): rows that existed before
    /// credentials are backfilled, not rewritten - password_hash = "!" (no usable
    /// password), must_change_password = true, security_version = 1,
    /// is_bootstrap_admin = false; status is untouched. The backfill defaults of
    /// the first three columns are dropped afterwards, so every new row must state
    /// its credential state. Down() is for development and test databases only:
    /// it drops these columns and their data (production goes forward instead).
    /// </summary>
    public partial class AddUserCredentialState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "!");

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "temporary_password_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "security_version",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // DEFAULT false stays (auth contract §11); the other defaults existed
            // only to backfill existing rows.
            migrationBuilder.AddColumn<bool>(
                name: "is_bootstrap_admin",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                ALTER TABLE users
                    ALTER COLUMN password_hash DROP DEFAULT,
                    ALTER COLUMN must_change_password DROP DEFAULT,
                    ALTER COLUMN security_version DROP DEFAULT;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_password_hash_not_empty",
                table: "users",
                sql: "password_hash <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_security_version_positive",
                table: "users",
                sql: "security_version >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_temporary_password_requires_change",
                table: "users",
                sql: "temporary_password_expires_at IS NULL OR must_change_password");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_bootstrap_admin_never_must_change",
                table: "users",
                sql: "NOT (is_bootstrap_admin AND must_change_password)");

            migrationBuilder.CreateIndex(
                name: "ux_users_is_bootstrap_admin",
                table: "users",
                column: "is_bootstrap_admin",
                unique: true,
                filter: "is_bootstrap_admin");

            // R20 / plan E7: the bootstrap flag is set only when the account is
            // created and never changed afterwards, whatever path writes to the
            // table. Same error shape as the ADR 0002 guards: check_violation with
            // the trigger's name as the constraint name.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION fn_users_forbid_bootstrap_flag_change()
                    RETURNS trigger
                    LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION USING
                        ERRCODE = 'check_violation',
                        CONSTRAINT = 'trg_users_forbid_bootstrap_flag_change',
                        MESSAGE = 'users.is_bootstrap_admin cannot be changed after the account is created.';
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_users_forbid_bootstrap_flag_change
                    BEFORE UPDATE OF is_bootstrap_admin ON users
                    FOR EACH ROW
                    WHEN (OLD.is_bootstrap_admin IS DISTINCT FROM NEW.is_bootstrap_admin)
                    EXECUTE FUNCTION fn_users_forbid_bootstrap_flag_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_users_forbid_bootstrap_flag_change ON users;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_users_forbid_bootstrap_flag_change();");

            migrationBuilder.DropIndex(
                name: "ux_users_is_bootstrap_admin",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_bootstrap_admin_never_must_change",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_temporary_password_requires_change",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_security_version_positive",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_password_hash_not_empty",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_bootstrap_admin",
                table: "users");

            migrationBuilder.DropColumn(
                name: "security_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "temporary_password_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "must_change_password",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "users");
        }
    }
}
