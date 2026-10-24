using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contractra.Infrastructure.Migrations
{
    /// <summary>
    /// Backend Sprint 2, Slice 6 (contract §22 M5): adds <c>ThrottleCleared</c> to
    /// the <c>audit_events.action_type</c> catalog for the operator command
    /// <c>clear-throttle --identifier &lt;text&gt; --reason &lt;text&gt;</c>.
    ///
    /// Additive: widens ck_audit_events_action_type_valid_values to the 16th
    /// value without touching any existing row (contract §22 I3: "a later rename
    /// adds a value and keeps the old one valid").
    /// </summary>
    public partial class AddThrottleClearedAuditAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_events_action_type_valid_values",
                table: "audit_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_events_action_type_valid_values",
                table: "audit_events",
                sql: "action_type IN ('UserCreated', 'UserDisabled', 'UserReactivated', 'RoleAssigned', 'RoleEnded', 'PermissionGranted', 'PermissionRevoked', 'PayrollViewGranted', 'PayrollViewRevoked', 'PasswordChanged', 'PasswordResetIssued', 'PasswordResetCompleted', 'InitialPasswordReissued', 'BootstrapPasswordReset', 'AccessDenied', 'ThrottleCleared')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_events_action_type_valid_values",
                table: "audit_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_events_action_type_valid_values",
                table: "audit_events",
                sql: "action_type IN ('UserCreated', 'UserDisabled', 'UserReactivated', 'RoleAssigned', 'RoleEnded', 'PermissionGranted', 'PermissionRevoked', 'PayrollViewGranted', 'PayrollViewRevoked', 'PasswordChanged', 'PasswordResetIssued', 'PasswordResetCompleted', 'InitialPasswordReissued', 'BootstrapPasswordReset', 'AccessDenied')");
        }
    }
}
