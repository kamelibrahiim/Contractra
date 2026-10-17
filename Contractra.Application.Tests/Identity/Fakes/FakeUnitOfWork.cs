using Contractra.Application.Common;

namespace Contractra.Application.Tests.Identity.Fakes;

/// <summary>Tracks begin/save-commit calls without a database, so tests can assert transaction discipline.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public bool TransactionOpen { get; private set; }

    public bool Committed { get; private set; }

    /// <summary>Starts a fresh unit of work, as a new DI scope would.</summary>
    public void Reset()
    {
        TransactionOpen = false;
        Committed = false;
    }

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (TransactionOpen)
        {
            throw new InvalidOperationException("A transaction is already open.");
        }

        TransactionOpen = true;
        return Task.CompletedTask;
    }

    public Task SaveChangesAndCommitAsync(CancellationToken cancellationToken = default)
    {
        if (!TransactionOpen)
        {
            throw new InvalidOperationException("No transaction is open.");
        }

        Committed = true;
        TransactionOpen = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
