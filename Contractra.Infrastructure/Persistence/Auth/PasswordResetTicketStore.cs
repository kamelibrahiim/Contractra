using Contractra.Application.Identity;
using Contractra.Domain.Identity;

namespace Contractra.Infrastructure.Persistence.Auth;

public sealed class PasswordResetTicketStore : IPasswordResetTicketStore
{
    private readonly ContractraDbContext _dbContext;

    public PasswordResetTicketStore(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<PasswordResetTicket?> FindOpenAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.PasswordResetTickets.FindOpenAsync(userId, cancellationToken);

    public Task<int> IssueAsync(PasswordResetTicket ticket, CancellationToken cancellationToken = default) =>
        _dbContext.IssuePasswordResetTicketAsync(ticket, cancellationToken);
}
