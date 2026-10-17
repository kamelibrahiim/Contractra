using Contractra.Application.Common;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Application.Identity.Auth;

public enum LogoutStatus
{
    /// <summary>204, also when the session was already revoked (contract §5.3).</summary>
    LoggedOut,

    /// <summary>A web session's logout without <c>X-CSRF-Protection: 1</c> (contract §8): 403, Serilog only.</summary>
    CsrfHeaderMissing,
}

/// <param name="Platform">The session's stored platform (null when the session does not exist), so the web response can clear the cookie.</param>
public sealed record LogoutResult(LogoutStatus Status, ClientPlatform? Platform);

/// <summary>
/// <c>POST /api/v1/auth/logout</c> (auth contract v1.5 §5.3, §8): revokes the session
/// named by the access token's <c>sid</c>. Web sessions must carry the CSRF header.
/// </summary>
public sealed class LogoutUseCase
{
    private readonly ISessionStore _sessions;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LogoutUseCase> _logger;

    public LogoutUseCase(ISessionStore sessions, IClock clock, IUnitOfWork unitOfWork, ILogger<LogoutUseCase>? logger = null)
    {
        _sessions = sessions;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<LogoutUseCase>.Instance;
    }

    public async Task<LogoutResult> ExecuteAsync(Guid userId, Guid sessionId, bool csrfHeaderPresent, CancellationToken cancellationToken = default)
    {
        var session = await _sessions.FindByIdAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId)
        {
            return new LogoutResult(LogoutStatus.LoggedOut, null);
        }

        if (session.ClientPlatform == ClientPlatform.Web && !csrfHeaderPresent)
        {
            return new LogoutResult(LogoutStatus.CsrfHeaderMissing, session.ClientPlatform);
        }

        if (session.RevokedAt is null)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            session.Revoke(SessionRevokeReason.Logout, _clock.UtcNow);
            await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);
            _logger.LogInformation("Auth.LoggedOut SessionId={SessionId} UserId={UserId}", session.Id, userId);
        }

        return new LogoutResult(LogoutStatus.LoggedOut, session.ClientPlatform);
    }
}
