using Contractra.Application.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace Contractra.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ContractraDbContext _dbContext;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already open on this unit of work.");
        }

        _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task SaveChangesAndCommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No transaction is open; call BeginTransactionAsync first.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }
    }
}
