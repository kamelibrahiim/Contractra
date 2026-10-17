using Contractra.Application.Common;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Application.Identity.Auth;

/// <summary>
/// <c>POST /api/v1/auth/refresh</c> (auth contract v1.5 §3.3, §3.6, §4, §5.2; owner
/// decisions R5, R17, R21, R35, R38) and the rotation of the current session after
/// a password change (§5.5, R36, E11).
///
/// Idle timeout and the 7-day absolute limit are checked here, at refresh only;
/// no other request writes to the session (§3.6). A concurrent rotation of the same
/// token loses at commit (ADR 0005); the HTTP layer retries the whole refresh once in
/// a fresh scope, which then takes the reuse-grace path.
/// </summary>
public sealed class RefreshUseCase
{
    private readonly IAuthSessionRepository _sessions;
    private readonly ISessionStore _sessionStore;
    private readonly IUserRepository _users;
    private readonly IUserAccessReader _access;
    private readonly IRefreshTokenFactory _refreshTokens;
    private readonly IAccessTokenIssuer _accessTokens;
    private readonly AuthSessionPolicy _policy;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RefreshUseCase> _logger;

    public RefreshUseCase(
        IAuthSessionRepository sessions,
        ISessionStore sessionStore,
        IUserRepository users,
        IUserAccessReader access,
        IRefreshTokenFactory refreshTokens,
        IAccessTokenIssuer accessTokens,
        AuthSessionPolicy policy,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<RefreshUseCase>? logger = null)
    {
        _sessions = sessions;
        _sessionStore = sessionStore;
        _users = users;
        _access = access;
        _refreshTokens = refreshTokens;
        _accessTokens = accessTokens;
        _policy = policy;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<RefreshUseCase>.Instance;
    }

    public async Task<AuthOutcome<SessionTokens>> ExecuteAsync(string? presentedToken, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        if (string.IsNullOrEmpty(presentedToken))
        {
            return Invalid("no_token", sessionId: null);
        }

        var presentedHash = _refreshTokens.Hash(presentedToken);
        var session = await _sessions.FindByRefreshTokenHashAsync(presentedHash, cancellationToken);
        if (session is null)
        {
            return Invalid("unknown_token", sessionId: null);
        }

        if (session.RevokedAt is not null)
        {
            return Invalid("session_revoked", session.Id);
        }

        // 7-day absolute limit: derived from absolute_expires_at, never written as a revocation.
        if (now >= session.AbsoluteExpiresAt)
        {
            return Expired("absolute_expiry", session.Id);
        }

        var presented = session.RefreshTokens.Single(t => t.TokenHash.AsSpan().SequenceEqual(presentedHash));

        var user = await _users.FindByIdAsync(session.UserId, cancellationToken);
        if (user is not { Status: UserStatus.Active })
        {
            // A disabled user's sessions were revoked when they were disabled (R5); refuse defensively.
            return Invalid("user_not_active", session.Id);
        }

        var access = await _access.GetAsync(user.Id, now, cancellationToken);

        if (presented.ConsumedAt is { } consumedAt)
        {
            if (now - consumedAt > AuthSessionPolicy.RefreshReuseGrace)
            {
                // Reuse after the grace: treated as theft, the whole session is revoked (R35).
                await RevokeAsync(session, SessionRevokeReason.RefreshReuse, now, cancellationToken);
                _logger.LogWarning("Auth.RefreshFailed Reason={Reason} SessionId={SessionId} UserId={UserId}", "refresh_reuse", session.Id, user.Id);
                return AuthOutcome<SessionTokens>.Fail(AuthFailure.Rejected(AuthErrorCodes.RefreshInvalid, "The refresh token is invalid."));
            }

            // Reuse within the grace (two tabs refreshing together): a new access token
            // only; the refresh token is not rotated again and the session is not revoked.
            if (!access.HasPermission(AppAccess.PermissionFor(session.ClientApp)))
            {
                await RevokeAsync(session, SessionRevokeReason.AppAccessLost, now, cancellationToken);
                return Invalid("app_access_lost", session.Id);
            }

            _logger.LogInformation("Auth.Refreshed SessionId={SessionId} UserId={UserId} WithinReuseGrace={Grace}", session.Id, user.Id, true);
            return AuthOutcome<SessionTokens>.Success(new SessionTokens(
                IssueAccessToken(user, session, access, now), null, session.AbsoluteExpiresAt, session.ClientPlatform, user.MustChangePassword));
        }

        // Idle timeout: the shortest among the user's active roles at refresh time (§3.6).
        if (now - session.LastRefreshedAt > _policy.IdleTimeoutFor(access.ActiveRoleCodes))
        {
            await RevokeAsync(session, SessionRevokeReason.Idle, now, cancellationToken);
            return Expired("idle_timeout", session.Id);
        }

        // The user must still hold the session's app-access permission (§4).
        if (!access.HasPermission(AppAccess.PermissionFor(session.ClientApp)))
        {
            await RevokeAsync(session, SessionRevokeReason.AppAccessLost, now, cancellationToken);
            return Invalid("app_access_lost", session.Id);
        }

        var next = _refreshTokens.Create();
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        session.Rotate(presentedHash, next.Hash, now);
        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);

        _logger.LogInformation("Auth.Refreshed SessionId={SessionId} UserId={UserId} WithinReuseGrace={Grace}", session.Id, user.Id, false);
        return AuthOutcome<SessionTokens>.Success(new SessionTokens(
            IssueAccessToken(user, session, access, now), next.Token, session.AbsoluteExpiresAt, session.ClientPlatform, user.MustChangePassword));
    }

    /// <summary>
    /// After a successful change-password (contract §5.5, E11): consumes the current
    /// (latest unconsumed) refresh token of the session named by the access token's
    /// <c>sid</c> and issues its replacement, with an access token carrying the new
    /// security version. Runs in its own transaction after ChangePasswordUseCase has
    /// committed (see ChangePasswordUseCase's transaction note).
    /// </summary>
    public async Task<AuthOutcome<SessionTokens>> RotateAfterPasswordChangeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var session = await _sessionStore.FindByIdAsync(sessionId, cancellationToken);
        var live = session?.RefreshTokens.SingleOrDefault(t => t.ConsumedAt is null);
        if (session is null || session.UserId != userId || !session.IsActiveAt(now) || live is null)
        {
            return Invalid("session_not_rotatable", sessionId);
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("The user of an active session must exist.");
        var access = await _access.GetAsync(userId, now, cancellationToken);

        var next = _refreshTokens.Create();
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        session.Rotate(live.TokenHash, next.Hash, now);
        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);

        return AuthOutcome<SessionTokens>.Success(new SessionTokens(
            IssueAccessToken(user, session, access, now), next.Token, session.AbsoluteExpiresAt, session.ClientPlatform, user.MustChangePassword));
    }

    private IssuedAccessToken IssueAccessToken(User user, AuthSession session, UserAccess access, DateTimeOffset now) =>
        _accessTokens.Issue(
            new AccessTokenSubject(user.Id, session.Id, session.ClientApp, user.SecurityVersion, access.EffectivePermissions, access.ActiveRoleCodes),
            now);

    private async Task RevokeAsync(AuthSession session, SessionRevokeReason reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        session.Revoke(reason, now);
        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);
    }

    private AuthOutcome<SessionTokens> Invalid(string reason, Guid? sessionId)
    {
        _logger.LogInformation("Auth.RefreshFailed Reason={Reason} SessionId={SessionId}", reason, sessionId);
        return AuthOutcome<SessionTokens>.Fail(AuthFailure.Rejected(AuthErrorCodes.RefreshInvalid, "The refresh token is invalid."));
    }

    private AuthOutcome<SessionTokens> Expired(string reason, Guid sessionId)
    {
        _logger.LogInformation("Auth.RefreshFailed Reason={Reason} SessionId={SessionId}", reason, sessionId);
        return AuthOutcome<SessionTokens>.Fail(AuthFailure.Rejected(AuthErrorCodes.SessionExpired, "The session has expired."));
    }
}
