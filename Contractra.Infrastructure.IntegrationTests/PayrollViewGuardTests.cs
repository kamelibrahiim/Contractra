using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// BR-235 / ح-235: Payroll.View is never granted through a role. These tests go
/// straight to SQL, bypassing the domain, to prove the database guard holds on
/// its own. Each test runs inside a transaction that is rolled back, because the
/// case-insensitive unique index allows only one Payroll.View-like code per
/// database. Role/permission codes used as test fixtures are suffixed with a
/// unique token (not the bare "Admin"/"OfficeStaff"/"Accountant"/"Payroll.View"
/// spelling) because Sprint 2's SeedAuthorizationCatalog migration now seeds
/// those exact codes as permanent, committed rows in every ephemeral test
/// database - a plain-name insert here would collide with the seed's rows on
/// ux_roles_code_lower / ux_permissions_code_lower before this test's own
/// transaction even begins, since rollback cannot undo the seed's earlier
/// commit. BR-235 does not depend on which role/permission names are used.
/// </summary>
public class PayrollViewGuardTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string GrantGuard = "trg_role_permissions_forbid_payroll_view";
    private const string RenameGuard = "trg_permissions_forbid_granted_rename_to_payroll_view";

    private readonly PostgresTestDatabaseFixture _fixture;

    public PayrollViewGuardTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("Payroll.View")]
    [InlineData("payroll.view")]
    [InlineData("PAYROLL.VIEW")]
    [InlineData("PayRoll.vIEW")]
    public async Task RawInsert_CannotGrantPayrollViewToARole_InAnyCasing(string payrollViewCode)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var roleId = await InsertRoleAsync(connection, transaction, UniqueCode("Admin"));

        // SeedAuthorizationCatalog already owns "Payroll.View" (case-insensitively)
        // as a permanent, committed row, so a fresh insert in any casing would
        // collide on ux_permissions_code_lower. Delete the seeded row inside this
        // test's own transaction (rolled back at the end, so it is restored for
        // every other test) so a fresh row can still be inserted in each of the
        // theory's casings, preserving the original proof that the trigger reads
        // a stored value correctly regardless of its exact casing - not just a
        // lookup's casing.
        await PostgresAssert.ExecuteAsync(
            connection, transaction, "DELETE FROM permissions WHERE lower(code) = 'payroll.view';");
        var permissionId = await InsertPermissionAsync(connection, transaction, payrollViewCode);

        await PostgresAssert.FailsWithAsync(
            () => InsertGrantAsync(connection, transaction, roleId, permissionId),
            "23514",
            GrantGuard);
    }

    [Fact]
    public async Task RawUpdate_CannotRepointAnExistingGrantToPayrollView()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var roleId = await InsertRoleAsync(connection, transaction, UniqueCode("Accountant"));
        var normalPermissionId = await InsertPermissionAsync(connection, transaction, UniqueCode("Payroll.Approve"));
        // SeedAuthorizationCatalog already owns the permanent "Payroll.View" row
        // (case-insensitively) in this database, so the trigger's guard is
        // proven here against that existing row instead of inserting a second one.
        var payrollViewId = await GetSeededPermissionIdAsync(connection, transaction, "Payroll.View");
        var grantId = await InsertGrantAsync(connection, transaction, roleId, normalPermissionId);

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(
                connection,
                transaction,
                "UPDATE role_permissions SET permission_id = @permission WHERE id = @id;",
                ("permission", payrollViewId),
                ("id", grantId)),
            "23514",
            GrantGuard);
    }

    [Theory]
    [InlineData("Payroll.View")]
    [InlineData("payroll.view")]
    [InlineData("PAYROLL.View")]
    public async Task RawUpdate_CannotRenameAGrantedPermissionToPayrollView(string payrollViewCode)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var roleId = await InsertRoleAsync(connection, transaction, UniqueCode("OfficeStaff"));
        var permissionId = await InsertPermissionAsync(connection, transaction, UniqueCode("Payroll.Prepare"));
        await InsertGrantAsync(connection, transaction, roleId, permissionId);

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(
                connection,
                transaction,
                "UPDATE permissions SET code = @code WHERE id = @id;",
                ("code", payrollViewCode),
                ("id", permissionId)),
            "23514",
            RenameGuard);
    }

    [Fact]
    public async Task RenameGuard_IsNotAGeneralImmutabilityRule()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var roleId = await InsertRoleAsync(connection, transaction, UniqueCode("Engineer"));
        var grantedId = await InsertPermissionAsync(connection, transaction, UniqueCode("Expenses.Create"));
        var ungrantedId = await InsertPermissionAsync(connection, transaction, UniqueCode("Payroll.Draft"));
        await InsertGrantAsync(connection, transaction, roleId, grantedId);

        // A granted permission may still be renamed to something other than Payroll.View...
        var renamedCode = UniqueCode("Expenses.Submit");
        Assert.Equal(1, await PostgresAssert.ExecuteAsync(
            connection, transaction, "UPDATE permissions SET code = @code WHERE id = @id;", ("code", renamedCode), ("id", grantedId)));

        // ...and a permission with no role grants may become Payroll.View.
        var payrollViewCode = UniqueCode("Payroll.View");
        Assert.Equal(1, await PostgresAssert.ExecuteAsync(
            connection, transaction, "UPDATE permissions SET code = @code WHERE id = @id;", ("code", payrollViewCode), ("id", ungrantedId)));
    }

    [Fact]
    public async Task NoSequenceOfDirectOperations_LeavesPayrollViewGrantedToARole()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var roleId = await InsertRoleAsync(connection, transaction, UniqueCode("Admin"));
        var otherRoleId = await InsertRoleAsync(connection, transaction, UniqueCode("Manager"));
        // SeedAuthorizationCatalog already owns the permanent "Payroll.View" row.
        var payrollViewId = await GetSeededPermissionIdAsync(connection, transaction, "Payroll.View");
        var normalId = await InsertPermissionAsync(connection, transaction, UniqueCode("Users.Manage"));
        var grantId = await InsertGrantAsync(connection, transaction, roleId, normalId);

        // Each attempt runs in a savepoint so the transaction survives the error.
        async Task AttemptAsync(string sql, params (string, object)[] parameters)
        {
            await transaction.SaveAsync("attempt");
            await PostgresAssert.FailsWithAsync(
                () => PostgresAssert.ExecuteAsync(connection, transaction, sql, parameters), "23514");
            await transaction.RollbackAsync("attempt");
        }

        await AttemptAsync(
            "INSERT INTO role_permissions (id, role_id, permission_id) VALUES (@id, @role, @permission);",
            ("id", Guid.CreateVersion7()), ("role", otherRoleId), ("permission", payrollViewId));
        await AttemptAsync(
            "UPDATE role_permissions SET permission_id = @permission WHERE id = @id;",
            ("permission", payrollViewId), ("id", grantId));
        await AttemptAsync(
            "UPDATE role_permissions SET permission_id = @permission, role_id = @role WHERE id = @id;",
            ("permission", payrollViewId), ("role", otherRoleId), ("id", grantId));
        await AttemptAsync(
            "UPDATE permissions SET code = 'PAYROLL.VIEW' WHERE id = @id;",
            ("id", normalId));

        Assert.Equal(0, await CountPayrollViewGrantsAsync(connection, transaction));
    }

    /// <summary>
    /// Rename and grant racing in two transactions: the grant trigger reads the
    /// permission row FOR SHARE, so the rename waits for the grant to commit and
    /// its own guard then sees the committed grant.
    /// </summary>
    [Fact]
    public async Task ConcurrentGrantAndRename_CannotTogetherGrantPayrollView()
    {
        Guid roleId;
        Guid permissionId;
        await using (var setup = await OpenConnectionAsync())
        {
            roleId = await InsertRoleAsync(setup, null, $"Race.{Guid.NewGuid():N}");
            permissionId = await InsertPermissionAsync(setup, null, $"Race.{Guid.NewGuid():N}");
        }

        await using var grantConnection = await OpenConnectionAsync();
        await using var renameConnection = await OpenConnectionAsync();

        await using var grantTransaction = await grantConnection.BeginTransactionAsync();
        await InsertGrantAsync(grantConnection, grantTransaction, roleId, permissionId);

        await using var renameTransaction = await renameConnection.BeginTransactionAsync();
        var rename = PostgresAssert.ExecuteAsync(
            renameConnection,
            renameTransaction,
            "UPDATE permissions SET code = 'Payroll.View' WHERE id = @id;",
            ("id", permissionId));

        // The rename must be blocked by the grant's row lock, not already finished.
        var finishedFirst = await Task.WhenAny(rename, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(rename, finishedFirst);

        await grantTransaction.CommitAsync();

        await PostgresAssert.FailsWithAsync(() => rename, "23514", RenameGuard);
        await renameTransaction.RollbackAsync();

        await using var verify = await OpenConnectionAsync();
        Assert.Equal(0, await CountPayrollViewGrantsAsync(verify, null));
    }

    // Appends a per-test-run unique suffix so a fixture code such as "Admin"
    // never collides with SeedAuthorizationCatalog's permanent, committed rows
    // (see class remarks).
    private static string UniqueCode(string baseName) => $"{baseName}.{Guid.NewGuid():N}";

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<Guid> InsertRoleAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string code)
    {
        var id = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO roles (id, code, status) VALUES (@id, @code, 'Active');",
            ("id", id),
            ("code", code));
        return id;
    }

    // Looks up a permission SeedAuthorizationCatalog already inserted (e.g.
    // "Payroll.View"), rather than inserting a duplicate that would collide on
    // ux_permissions_code_lower.
    private static async Task<Guid> GetSeededPermissionIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string code)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id FROM permissions WHERE lower(code) = lower(@code);", connection, transaction);
        command.Parameters.AddWithValue("code", code);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Guid> InsertPermissionAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string code)
    {
        var id = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO permissions (id, code, status) VALUES (@id, @code, 'Active');",
            ("id", id),
            ("code", code));
        return id;
    }

    private static async Task<Guid> InsertGrantAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid roleId, Guid permissionId)
    {
        var id = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO role_permissions (id, role_id, permission_id) VALUES (@id, @role, @permission);",
            ("id", id),
            ("role", roleId),
            ("permission", permissionId));
        return id;
    }

    private static Task<long> CountPayrollViewGrantsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction) =>
        PostgresAssert.CountAsync(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM role_permissions rp
            JOIN permissions p ON p.id = rp.permission_id
            WHERE lower(p.code) = 'payroll.view';
            """);
}
