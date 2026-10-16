using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Audit;

/// <summary>
/// What the connected database role can do to one audit table (repository ADR 0006
/// §7.3). The four privileges are exactly the ones the migration revokes from every
/// non-owner grantee.
/// </summary>
public sealed record AuditPrivilegeReport(
    string Table,
    bool CanUpdate,
    bool CanDelete,
    bool CanTruncate,
    bool CanTrigger,
    bool IsOwner,
    bool HasOwnerRoleMembership,
    bool IsSuperuser)
{
    /// <summary>
    /// True when the role could change or remove audit rows, or attach a trigger:
    /// directly (the four privileges, which PostgreSQL resolves through inherited
    /// membership on its own), by being the owner, or by holding any non-owner
    /// membership in the owner role. Membership is treated as unsafe deliberately,
    /// whether or not it is currently usable (<c>INHERIT</c> or an active
    /// <c>SET ROLE</c>): a <c>NOINHERIT</c> member can still reach the owner's
    /// privileges with <c>SET ROLE</c> at any time, which the four direct checks
    /// would not catch, so <see cref="HasOwnerRoleMembership"/> is conservative on
    /// purpose rather than narrowed to only currently-usable membership.
    /// </summary>
    public bool IsUnsafe => CanUpdate || CanDelete || CanTruncate || CanTrigger || IsOwner || HasOwnerRoleMembership || IsSuperuser;
}

/// <summary>
/// The runtime privilege self-check query. Slice 3 delivers the query and its test;
/// the startup wiring that fails closed in Production arrives with Slice 5 (ADR 0006
/// E42). Read-only.
/// </summary>
public static class AuditPrivilegeSelfCheck
{
    public static readonly IReadOnlyList<string> AuditTables = ["audit_events"];

    private const string Sql = """
        SELECT has_table_privilege(current_user, c.oid, 'UPDATE'),
               has_table_privilege(current_user, c.oid, 'DELETE'),
               has_table_privilege(current_user, c.oid, 'TRUNCATE'),
               has_table_privilege(current_user, c.oid, 'TRIGGER'),
               c.relowner = (SELECT oid FROM pg_roles WHERE rolname = current_user),
               pg_has_role(current_user, c.relowner, 'MEMBER') AND c.relowner <> (SELECT oid FROM pg_roles WHERE rolname = current_user),
               (SELECT rolsuper FROM pg_roles WHERE rolname = current_user)
        FROM pg_class c
        WHERE c.oid = to_regclass(@table);
        """;

    public static async Task<IReadOnlyList<AuditPrivilegeReport>> QueryAsync(
        ContractraDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var connection = dbContext.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var reports = new List<AuditPrivilegeReport>();
            foreach (var table in AuditTables)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = Sql;
                var parameter = command.CreateParameter();
                parameter.ParameterName = "table";
                parameter.Value = table;
                command.Parameters.Add(parameter);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException($"Audit table '{table}' does not exist.");
                }

                reports.Add(new AuditPrivilegeReport(
                    table,
                    reader.GetBoolean(0),
                    reader.GetBoolean(1),
                    reader.GetBoolean(2),
                    reader.GetBoolean(3),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6)));
            }

            return reports;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }
}
