using Contractra.Application.Identity;
using Contractra.Domain.Identity;

namespace Contractra.Infrastructure.Persistence.Auth;

public sealed class SessionStore : ISessionStore
{
    private readonly ContractraDbContext _dbContext;

    public SessionStore(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<AuthSession?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        _dbContext.AuthSessions.FindByIdAsync(sessionId, cancellationToken);

    public Task<int> RevokeAllActiveAsync(
        Guid userId,
        SessionRevokeReason reason,
        DateTimeOffset revokedAt,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default) =>
        _dbContext.AuthSessions.RevokeAllActiveAsync(userId, reason, revokedAt, exceptSessionId, cancellationToken);
}
