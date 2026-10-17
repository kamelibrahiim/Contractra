using Contractra.Domain.Identity;

namespace Contractra.Application.Identity;

/// <summary>Read access to the Role catalog needed by CreateUser (contract §9.1).</summary>
public interface IRoleRepository
{
    /// <summary>Case-insensitive lookup by code, or null when no role has that code.</summary>
    Task<Role?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
}
