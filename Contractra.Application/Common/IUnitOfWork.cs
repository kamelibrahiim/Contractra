namespace Contractra.Application.Common;

/// <summary>
/// One transaction per use case (ADR 0006 §6, §12): opened before the first
/// write, saved and committed through a single exit. A use case disposes it
/// with <c>await using</c>; a failure (an exception from any step, including
/// <see cref="SaveChangesAndCommitAsync"/>) leaves it to Dispose, which rolls
/// back every staged change, including audit events, instead of committing.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves every pending change, then commits the open transaction.</summary>
    Task SaveChangesAndCommitAsync(CancellationToken cancellationToken = default);
}
