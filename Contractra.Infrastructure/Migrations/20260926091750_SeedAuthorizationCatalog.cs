using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <summary>
    /// Seeds the reference data needed by the Identity/Authorization
    /// functionality that already exists: the four roles (spec §7.1) and six
    /// permissions - the four Identity permissions (Confirmed Decision C2) and
    /// the two app-access permissions Apps.Admin.Access / Apps.Employee.Access
    /// (owner decision R13, not in spec §7.3) - plus seven role_permissions
    /// rows (docs/implementation/backend/features/admin-user-creation-plan.md
    /// §1B). Payroll.View is deliberately seeded as a permission but granted
    /// to no role (BR-235 / Confirmed Decision C3) - the existing
    /// trg_role_permissions_forbid_payroll_view trigger (ADR 0002 §6.2) would
    /// reject an attempted role grant, so none is attempted here.
    /// </summary>
    public partial class SeedAuthorizationCatalog : Migration
    {
        // Fixed, deterministic IDs so this migration is reproducible.
        private static readonly Guid RoleEngineerId = new("01930000-0000-7000-8000-000000000001");
        private static readonly Guid RoleOfficeStaffId = new("01930000-0000-7000-8000-000000000002");
        private static readonly Guid RoleAccountantId = new("01930000-0000-7000-8000-000000000003");
        private static readonly Guid RoleAdminId = new("01930000-0000-7000-8000-000000000004");

        private static readonly Guid PermissionUsersManageId = new("01930000-0000-7000-8000-000000000011");
        private static readonly Guid PermissionPermissionsGrantId = new("01930000-0000-7000-8000-000000000012");
        private static readonly Guid PermissionRolesManageId = new("01930000-0000-7000-8000-000000000013");
        private static readonly Guid PermissionPayrollViewId = new("01930000-0000-7000-8000-000000000014");
        private static readonly Guid PermissionAppsAdminAccessId = new("01930000-0000-7000-8000-000000000015");
        private static readonly Guid PermissionAppsEmployeeAccessId = new("01930000-0000-7000-8000-000000000016");

        private static readonly Guid RolePermissionAdminUsersManageId = new("01930000-0000-7000-8000-000000000021");
        private static readonly Guid RolePermissionAdminPermissionsGrantId = new("01930000-0000-7000-8000-000000000022");
        private static readonly Guid RolePermissionAdminRolesManageId = new("01930000-0000-7000-8000-000000000023");
        private static readonly Guid RolePermissionAdminAppsAdminAccessId = new("01930000-0000-7000-8000-000000000024");
        private static readonly Guid RolePermissionOfficeStaffAppsAdminAccessId = new("01930000-0000-7000-8000-000000000025");
        private static readonly Guid RolePermissionAccountantAppsAdminAccessId = new("01930000-0000-7000-8000-000000000026");
        private static readonly Guid RolePermissionEngineerAppsEmployeeAccessId = new("01930000-0000-7000-8000-000000000027");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "code", "status" },
                columnTypes: new[] { "uuid", "text", "text" },
                values: new object[,]
                {
                    { RoleEngineerId, "Engineer", "Active" },
                    { RoleOfficeStaffId, "OfficeStaff", "Active" },
                    { RoleAccountantId, "Accountant", "Active" },
                    { RoleAdminId, "Admin", "Active" },
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "code", "status" },
                columnTypes: new[] { "uuid", "text", "text" },
                values: new object[,]
                {
                    { PermissionUsersManageId, "Users.Manage", "Active" },
                    { PermissionPermissionsGrantId, "Permissions.Grant", "Active" },
                    { PermissionRolesManageId, "Roles.Manage", "Active" },
                    { PermissionPayrollViewId, "Payroll.View", "Active" },
                    { PermissionAppsAdminAccessId, "Apps.Admin.Access", "Active" },
                    { PermissionAppsEmployeeAccessId, "Apps.Employee.Access", "Active" },
                });

            // Identity permissions: Admin only (spec §7.3). App access: owner
            // decision R13. No row for Payroll.View under any role - see
            // class-level remarks.
            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "id", "role_id", "permission_id" },
                columnTypes: new[] { "uuid", "uuid", "uuid" },
                values: new object[,]
                {
                    { RolePermissionAdminUsersManageId, RoleAdminId, PermissionUsersManageId },
                    { RolePermissionAdminPermissionsGrantId, RoleAdminId, PermissionPermissionsGrantId },
                    { RolePermissionAdminRolesManageId, RoleAdminId, PermissionRolesManageId },
                    { RolePermissionAdminAppsAdminAccessId, RoleAdminId, PermissionAppsAdminAccessId },
                    { RolePermissionOfficeStaffAppsAdminAccessId, RoleOfficeStaffId, PermissionAppsAdminAccessId },
                    { RolePermissionAccountantAppsAdminAccessId, RoleAccountantId, PermissionAppsAdminAccessId },
                    { RolePermissionEngineerAppsEmployeeAccessId, RoleEngineerId, PermissionAppsEmployeeAccessId },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "role_permissions", keyColumn: "id", keyValue: RolePermissionAdminUsersManageId);
            migrationBuilder.DeleteData(table: "role_permissions", keyColumn: "id", keyValue: RolePermissionAdminPermissionsGrantId);
            migrationBuilder.DeleteData(table: "role_permissions", keyColumn: "id", keyValue: RolePermissionAdminRolesManageId);
            migrationBuilder.DeleteData(table: "role_permissions", keyColumn: "id", keyValue: RolePermissionAdminAppsAdminAccessId);
            migrationBuilder.DeleteData(table: "role_permissions", keyColumn: "id", keyValue: RolePermissionOfficeStaffAppsAdminAccessId);
            migrationBuilder.DeleteData(table: "role_permissions", keyColumn: "id", keyValue: RolePermissionAccountantAppsAdminAccessId);
            migrationBuilder.DeleteData(table: "role_permissions", keyColumn: "id", keyValue: RolePermissionEngineerAppsEmployeeAccessId);

            migrationBuilder.DeleteData(table: "permissions", keyColumn: "id", keyValue: PermissionUsersManageId);
            migrationBuilder.DeleteData(table: "permissions", keyColumn: "id", keyValue: PermissionPermissionsGrantId);
            migrationBuilder.DeleteData(table: "permissions", keyColumn: "id", keyValue: PermissionRolesManageId);
            migrationBuilder.DeleteData(table: "permissions", keyColumn: "id", keyValue: PermissionPayrollViewId);
            migrationBuilder.DeleteData(table: "permissions", keyColumn: "id", keyValue: PermissionAppsAdminAccessId);
            migrationBuilder.DeleteData(table: "permissions", keyColumn: "id", keyValue: PermissionAppsEmployeeAccessId);

            migrationBuilder.DeleteData(table: "roles", keyColumn: "id", keyValue: RoleEngineerId);
            migrationBuilder.DeleteData(table: "roles", keyColumn: "id", keyValue: RoleOfficeStaffId);
            migrationBuilder.DeleteData(table: "roles", keyColumn: "id", keyValue: RoleAccountantId);
            migrationBuilder.DeleteData(table: "roles", keyColumn: "id", keyValue: RoleAdminId);
        }
    }
}
