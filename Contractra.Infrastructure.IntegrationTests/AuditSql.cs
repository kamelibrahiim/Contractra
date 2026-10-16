using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Raw-SQL builders for audit_events tests that go around the Domain on purpose,
/// to test the database rather than <c>AuditEvent</c>.
/// </summary>
internal static class AuditSql
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A valid System event (seeder-like): SystemJob source, keyed.</summary>
    public static Dictionary<string, object?> SystemRow() => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["occurred_at_utc"] = T0,
        ["actor_type"] = "System",
        ["actor_user_id"] = null,
        ["actor_role_code"] = null,
        ["action_type"] = "UserCreated",
        ["entity_type"] = "User",
        ["entity_id"] = Guid.CreateVersion7(),
        ["correlation_id"] = "corr-1",
        ["source"] = "SystemJob",
        ["idempotency_key"] = $"UserCreated:bootstrap:{Guid.NewGuid():N}",
        ["schema_version"] = 1,
    };

    /// <summary>A valid User event: Web source, no key.</summary>
    public static Dictionary<string, object?> UserRow(Guid userId) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["occurred_at_utc"] = T0,
        ["actor_type"] = "User",
        ["actor_user_id"] = userId,
        ["actor_role_code"] = "Admin",
        ["action_type"] = "PasswordChanged",
        ["entity_type"] = "User",
        ["entity_id"] = userId,
        ["correlation_id"] = "corr-1",
        ["source"] = "Web",
        ["idempotency_key"] = null,
        ["schema_version"] = 1,
    };

    public static Dictionary<string, object?> With(this Dictionary<string, object?> row, params (string Column, object? Value)[] overrides)
    {
        var copy = new Dictionary<string, object?>(row);
        foreach (var (column, value) in overrides)
        {
            copy[column] = value;
        }

        return copy;
    }

    public static async Task InsertAsync(NpgsqlConnection connection, Dictionary<string, object?> row)
    {
        var columns = row.Keys.ToArray();
        var values = columns.Select(c => c == "snapshot" ? $"CAST(@{c} AS jsonb)" : $"@{c}");
        await using var command = new NpgsqlCommand(
            $"INSERT INTO audit_events ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)});", connection);
        foreach (var column in columns)
        {
            command.Parameters.AddWithValue(column, row[column] ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public static async Task<NpgsqlException> FailsAsync(Func<Task> action, string sqlState, string? constraintName = null)
    {
        var exception = await Record.ExceptionAsync(action);
        var postgres = Assert.IsAssignableFrom<PostgresException>(
            exception is Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: { } inner } ? inner : exception);

        Assert.Equal(sqlState, postgres.SqlState);
        if (constraintName is not null)
        {
            Assert.Equal(constraintName, postgres.ConstraintName);
        }

        return postgres;
    }
}
