using Contractra.Domain.Identity;

namespace Contractra.Application.Identity;

/// <summary>
/// Session revocation needed by the Slice 4 use cases (ADR 0005 §4; ADR 0006
/// §6). Must run inside the caller's open transaction, alongside the rest of
/// the use case's writes; implementations throw if none is open.
/// </summary>
public interface ISessionStore
{
    /// <summary>The session with this id, tracked and ready to Rotate or Revoke, or null.</summary>
    Task<AuthSession?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every active session of the user, except
    /// <paramref name="exceptSessionId"/> when given. Returns the number of
    /// sessions revoked.
    /// </summary>
    Task<int> RevokeAllActiveAsync(
        Guid userId,
        SessionRevokeReason reason,
        DateTimeOffset revokedAt,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default);
}
