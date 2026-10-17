using Contractra.Domain.Identity;

namespace Contractra.Application.Identity;

/// <summary>
/// Persistence access to <see cref="User"/> needed by the Slice 4 use cases.
/// Tracked results are expected to be saved through the caller's unit of
/// work, not through this interface.
/// </summary>
public interface IUserRepository
{
    /// <summary>Case-insensitive lookup by login identifier (ADR 0003 E10), or null.</summary>
    Task<User?> FindByLoginIdentifierAsync(string loginIdentifier, CancellationToken cancellationToken = default);

    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True when any user's login identifier matches case-insensitively.</summary>
    Task<bool> ExistsWithLoginIdentifierAsync(string loginIdentifier, CancellationToken cancellationToken = default);

    void Add(User user);
}
