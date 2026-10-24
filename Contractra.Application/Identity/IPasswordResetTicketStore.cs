using Contractra.Domain.Identity;

namespace Contractra.Application.Identity;

/// <summary>
/// Reset-ticket persistence needed by IssuePasswordReset and
/// CompletePasswordReset (contract §5.6, §9.6, §10A; ADR 0005 §3.3; ADR 0006
/// §6). Issuing must run inside the caller's open transaction, alongside the
/// rest of the use case's writes; the implementation throws if none is open.
/// </summary>
public interface IPasswordResetTicketStore
{
    /// <summary>The user's open ticket (at most one exists), or null.</summary>
    Task<PasswordResetTicket?> FindOpenAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supersedes every open ticket of the user and inserts the new one, in
    /// the caller's open transaction. Returns the number of tickets
    /// superseded. Throws <see cref="InvalidOperationException"/> if no
    /// transaction is open.
    /// </summary>
    Task<int> IssueAsync(PasswordResetTicket ticket, CancellationToken cancellationToken = default);
}
