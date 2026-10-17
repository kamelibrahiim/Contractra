using Contractra.Application.Identity;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakePermissionRepository : IPermissionRepository
{
    private readonly List<Permission> _permissions = new();

    public void Seed(Permission permission) => _permissions.Add(permission);

    public Task<Permission?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(_permissions.SingleOrDefault(p => p.Code.Value == code));
}
