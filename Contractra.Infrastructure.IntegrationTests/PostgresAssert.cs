using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

internal static class PostgresAssert
{
    /// <summary>
    /// Asserts that the action fails with the given SQLSTATE and, when supplied,
    /// the given constraint/trigger name - whether it surfaces directly from
    /// Npgsql or wrapped by EF Core.
    /// </summary>
    public static async Task<PostgresException> FailsWithAsync(
        Func<Task> action,
        string sqlState,
        string? constraintName = null)
    {
        var exception = await Record.ExceptionAsync(action);

        var postgresException = exception switch
        {
            PostgresException pg => pg,
            DbUpdateException { InnerException: PostgresException pg } => pg,
            _ => throw new Xunit.Sdk.XunitException(
                $"Expected a PostgreSQL error {sqlState}, got: {exception?.GetType().Name ?? "no exception"} {exception?.Message}"),
        };

        Assert.Equal(sqlState, postgresException.SqlState);
        if (constraintName is not null)
        {
            Assert.Equal(constraintName, postgresException.ConstraintName);
        }

        return postgresException;
    }

    public static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<long> CountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }
}
