using Contractra.Application.Identity;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakeRoleRepository : IRoleRepository
{
    private readonly List<Role> _roles = new();

    public void Seed(Role role) => _roles.Add(role);

    public Task<Role?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(_roles.SingleOrDefault(r => string.Equals(r.Code.Value, code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<string>> ListActiveCodesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(_roles.Where(r => r.Status == RoleStatus.Active).Select(r => r.Code.Value).ToList());
}
