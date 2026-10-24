using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Auth;

/// <summary>
/// Reset-ticket persistence (auth contract v1.5 §5.6, §9.6, §10A; ADR 0005
/// §3.3, §4). Completing a reset is <see cref="PasswordResetTicket.Consume"/>
/// followed by SaveChanges: consumed_at and superseded_at are concurrency
/// tokens, so of two completions, or a completion and a new issue, only one
/// commits. A failed code check is a read-only outcome.
/// </summary>
public static class PasswordResetTicketPersistence
{
    /// <summary>The user's open ticket (at most one exists), tracked; expired or not.</summary>
    public static Task<PasswordResetTicket?> FindOpenAsync(
        this DbSet<PasswordResetTicket> tickets,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tickets);

        return tickets.SingleOrDefaultAsync(
            t => t.UserId == userId && t.ConsumedAt == null && t.SupersededAt == null, cancellationToken);
    }

    /// <summary>
    /// Supersedes every open ticket of the user at the new ticket's issue time
    /// (expired ones included) and saves the new ticket, in the caller's open
    /// transaction - which the caller must not commit if this throws (ADR 0005
    /// E25). SaveChanges also saves the context's other pending changes (in
    /// Slice 4, the user's BeginPasswordReset). A concurrent issue for the same
    /// user loses on users.security_version or on the deferred one-open-ticket
    /// constraint at commit. Returns the number of tickets superseded. Throws
    /// <see cref="InvalidOperationException"/> if no transaction is open
    /// (ADR 0006 §6: this writes alongside the rest of its use case's changes,
    /// so it must never run standalone).
    /// </summary>
    public static async Task<int> IssuePasswordResetTicketAsync(
        this ContractraDbContext dbContext,
        PasswordResetTicket ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(ticket);

        if (!ticket.IsOpen)
        {
            throw new ArgumentException("Only an open ticket can be issued.", nameof(ticket));
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "IssuePasswordResetTicketAsync writes alongside the rest of its use case's changes; the caller must open a transaction first (ADR 0006 §6).");
        }

        return await SupersedeAndInsertAsync(dbContext, ticket, cancellationToken);
    }

    private static async Task<int> SupersedeAndInsertAsync(
        ContractraDbContext dbContext,
        PasswordResetTicket ticket,
        CancellationToken cancellationToken)
    {
        var superseded = await dbContext.PasswordResetTickets
            .Where(t => t.UserId == ticket.UserId && t.ConsumedAt == null && t.SupersededAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.SupersededAt, (DateTimeOffset?)ticket.IssuedAt), cancellationToken);

        dbContext.PasswordResetTickets.Add(ticket);
        await dbContext.SaveChangesAsync(cancellationToken);
        return superseded;
    }
}
