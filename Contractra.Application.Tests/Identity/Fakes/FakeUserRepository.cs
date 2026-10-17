using Contractra.Application.Identity;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = new();

    public void Seed(User user) => _users[user.Id] = user;

    public IReadOnlyList<User> AllUsers() => _users.Values.ToList();

    public Task<User?> FindByLoginIdentifierAsync(string loginIdentifier, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.SingleOrDefault(u =>
            string.Equals(u.LoginIdentifier.Value, loginIdentifier, StringComparison.OrdinalIgnoreCase)));

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.GetValueOrDefault(id));

    public Task<bool> ExistsWithLoginIdentifierAsync(string loginIdentifier, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.Any(u =>
            string.Equals(u.LoginIdentifier.Value, loginIdentifier, StringComparison.OrdinalIgnoreCase)));

    public void Add(User user) => _users[user.Id] = user;

    private readonly Dictionary<string, Guid> _roleIdsByCode = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lets the fake resolve a role code to the id its assignments carry.</summary>
    public void RegisterRole(Role role) => _roleIdsByCode[role.Code.Value] = role.Id;

    public Task<bool> AnyActiveUserWithRoleAsync(string roleCode, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _roleIdsByCode.TryGetValue(roleCode, out var roleId)
            && _users.Values.Any(u => u.Status == UserStatus.Active && u.Roles.Any(r => r.RoleId == roleId && r.IsActiveAt(asOf))));

    public Task<User?> FindBootstrapAdminAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.SingleOrDefault(u => u.IsBootstrapAdmin));
}
