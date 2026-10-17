using Contractra.Domain.Identity;

namespace Contractra.Application.Identity;

/// <summary>Read access to the Permission catalog needed by the bootstrap seeder (contract §11).</summary>
public interface IPermissionRepository
{
    /// <summary>Lookup by exact permission code, or null.</summary>
    Task<Permission?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
}
