using Contractra.Application.Identity;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakePasswordResetTicketStore : IPasswordResetTicketStore
{
    private readonly List<PasswordResetTicket> _tickets = new();

    public void Seed(PasswordResetTicket ticket) => _tickets.Add(ticket);

    public Task<PasswordResetTicket?> FindOpenAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tickets.SingleOrDefault(t => t.UserId == userId && t.IsOpen));

    public Task<int> IssueAsync(PasswordResetTicket ticket, CancellationToken cancellationToken = default)
    {
        var superseded = 0;
        foreach (var existing in _tickets.Where(t => t.UserId == ticket.UserId && t.IsOpen))
        {
            existing.Supersede(ticket.IssuedAt);
            superseded++;
        }

        _tickets.Add(ticket);
        return Task.FromResult(superseded);
    }
}
