using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Runs operations so they start together (released by one gate, each on its
/// own DbContext/connection), and classifies the errors a losing concurrent
/// writer is expected to get.
/// </summary>
internal static class Concurrently
{
    /// <summary>Conflict SQLSTATEs: unique, exclusion, deadlock between deferred checks.</summary>
    public static readonly string[] ConflictSqlStates = { "23505", "23P01", "40P01" };

    public static async Task<Exception?[]> RunAsync(int count, Func<int, Task> operation)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await gate.Task;
            try
            {
                await operation(i);
                return (Exception?)null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }).ToList();

        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    public static bool IsConflict(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return true;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && ConflictSqlStates.Contains(postgres.SqlState))
            {
                return true;
            }
        }

        return false;
    }
}
