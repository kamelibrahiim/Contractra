using Contractra.Application.Identity.Auth;
using Contractra.Domain.Identity;

namespace Contractra.Infrastructure.Persistence.Auth;

public sealed class AuthSessionRepository : IAuthSessionRepository
{
    private readonly ContractraDbContext _dbContext;

    public AuthSessionRepository(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(AuthSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _dbContext.AuthSessions.Add(session);
    }

    public Task<AuthSession?> FindByRefreshTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default) =>
        _dbContext.AuthSessions.FindByRefreshTokenHashAsync(tokenHash, cancellationToken);
}
