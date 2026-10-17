using Contractra.Application.Identity;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = new();

    public void Seed(User user) => _users[user.Id] = user;

    public Task<User?> FindByLoginIdentifierAsync(string loginIdentifier, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.SingleOrDefault(u =>
            string.Equals(u.LoginIdentifier.Value, loginIdentifier, StringComparison.OrdinalIgnoreCase)));

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.GetValueOrDefault(id));

    public Task<bool> ExistsWithLoginIdentifierAsync(string loginIdentifier, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.Any(u =>
            string.Equals(u.LoginIdentifier.Value, loginIdentifier, StringComparison.OrdinalIgnoreCase)));

    public void Add(User user) => _users[user.Id] = user;
}
