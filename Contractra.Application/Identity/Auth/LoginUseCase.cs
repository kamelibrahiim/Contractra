using Contractra.Application.Common;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Application.Identity.Auth;

/// <param name="ClientPlatformHeader">The raw <c>X-Client-Platform</c> value, or null when absent (contract §5.1).</param>
/// <param name="ClientSource">The client IP after the trusted-proxy rules (contract §17), or "unknown".</param>
public sealed record LoginRequest(
    string? LoginIdentifier,
    string? Password,
    string? ClientApp,
    string? ClientPlatformHeader,
    string ClientSource,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

/// <summary>
/// <c>POST /api/v1/auth/login</c> (auth contract v1.5 §5.1, §4, §17; owner decisions
/// R11, R13, R23, R33, R38, R42, R45). Checks, in order: throttling → request shape
/// and input limits → user lookup → user Active → password correct and usable →
/// app-access permission. Every credential failure is the same generic
/// <c>auth.invalid_credentials</c>; the real reason goes to Serilog only.
/// </summary>
public sealed class LoginUseCase
{
    public const int MaxLoginIdentifierLength = 256;
    public const int MaxPasswordLength = 128;

    private readonly IUserRepository _users;
    private readonly IUserAccessReader _access;
    private readonly IPasswordHasher _hasher;
    private readonly DummyPasswordHash _dummyHash;
    private readonly IPasswordRehashStore _rehash;
    private readonly IAuthSessionRepository _sessions;
    private readonly IRefreshTokenFactory _refreshTokens;
    private readonly IAccessTokenIssuer _accessTokens;
    private readonly IAuthThrottle _throttle;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LoginUseCase> _logger;

    public LoginUseCase(
        IUserRepository users,
        IUserAccessReader access,
        IPasswordHasher hasher,
        DummyPasswordHash dummyHash,
        IPasswordRehashStore rehash,
        IAuthSessionRepository sessions,
        IRefreshTokenFactory refreshTokens,
        IAccessTokenIssuer accessTokens,
        IAuthThrottle throttle,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<LoginUseCase>? logger = null)
    {
        _users = users;
        _access = access;
        _hasher = hasher;
        _dummyHash = dummyHash;
        _rehash = rehash;
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _accessTokens = accessTokens;
        _throttle = throttle;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<LoginUseCase>.Instance;
    }

    public async Task<AuthOutcome<SessionTokens>> ExecuteAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        // 1. Throttling (contract §17) - before any lookup or hashing. Needs the identifier key.
        ThrottleTicket? ticket = null;
        string? identifierKeyHex = null;
        if (!string.IsNullOrEmpty(request.LoginIdentifier))
        {
            var identifierKey = ThrottleKeys.IdentifierKey(request.LoginIdentifier);
            identifierKeyHex = ThrottleKeys.ToHex(identifierKey);
            var admission = await _throttle.AdmitCredentialAttemptAsync(identifierKey, request.ClientSource, now, cancellationToken);
            switch (admission.Status)
            {
                case ThrottleStatus.Throttled:
                    return AuthOutcome<SessionTokens>.Fail(AuthFailure.Throttled(admission.RetryAfter!.Value));
                case ThrottleStatus.Unavailable:
                    return AuthOutcome<SessionTokens>.Fail(AuthFailure.ThrottleUnavailable());
            }

            ticket = admission.Ticket;
        }

        // 2. Request shape (400) and input limits (generic 401, no field hint - contract §1).
        var errors = new Dictionary<string, string[]>(request.FieldErrors ?? new Dictionary<string, string[]>());
        if (string.IsNullOrEmpty(request.LoginIdentifier) && !errors.ContainsKey("loginIdentifier"))
        {
            errors["loginIdentifier"] = ["The login identifier is required."];
        }

        if (string.IsNullOrEmpty(request.Password) && !errors.ContainsKey("password"))
        {
            errors["password"] = ["The password is required."];
        }

        var clientApp = AppAccess.Parse(request.ClientApp);
        if (clientApp is null && !errors.ContainsKey("clientApp"))
        {
            errors["clientApp"] = ["The client app must be 'admin' or 'employee'."];
        }

        ClientPlatform? platform = request.ClientPlatformHeader switch
        {
            null => ClientPlatform.Mobile,
            "web" => ClientPlatform.Web,
            "mobile" => ClientPlatform.Mobile,
            _ => null,
        };
        if (platform is null)
        {
            errors["X-Client-Platform"] = ["The client platform must be 'web' or 'mobile'."];
        }

        if (errors.Count > 0)
        {
            _logger.LogInformation(
                "Auth.LoginFailed Reason={Reason} IdentifierKey={IdentifierKey} Fields={Fields}",
                "validation_failed", identifierKeyHex, string.Join(",", errors.Keys));
            return AuthOutcome<SessionTokens>.Fail(AuthFailure.Validation(errors));
        }

        var identifier = request.LoginIdentifier!;
        var password = request.Password!;
        if (identifier.Length > MaxLoginIdentifierLength || password.Length > MaxPasswordLength)
        {
            return InvalidCredentials("input_over_limit", identifierKeyHex, userId: null);
        }

        // 3-5. Lookup, Active, usable and correct password - one hash verification in every case (R42).
        var user = await _users.FindByLoginIdentifierAsync(identifier, cancellationToken);
        string? reason = user switch
        {
            null => "unknown_identifier",
            { Status: not UserStatus.Active } => "account_disabled",
            { HasUsablePassword: false } => "no_usable_password",
            _ => null,
        };

        var verifiedHash = reason is null ? user!.PasswordHash : _dummyHash.Value;
        var verification = _hasher.Verify(verifiedHash, password);
        if (reason is not null)
        {
            return InvalidCredentials(reason, identifierKeyHex, user?.Id);
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            return InvalidCredentials("wrong_password", identifierKeyHex, user!.Id);
        }

        if (user!.MustChangePassword && user.TemporaryPasswordExpiresAt is { } expiresAt && expiresAt <= now)
        {
            return InvalidCredentials("temporary_password_expired", identifierKeyHex, user.Id);
        }

        // The password is correct: for brute-force purposes this attempt succeeded (contract §17).
        if (ticket is not null)
        {
            await _throttle.RecordCredentialSuccessAsync(ticket, cancellationToken);
        }

        // 6. App-access permission, only after the password was verified (contract §4).
        var access = await _access.GetAsync(user.Id, now, cancellationToken);
        var requiredPermission = AppAccess.PermissionFor(clientApp!.Value);
        if (!access.HasPermission(requiredPermission))
        {
            _logger.LogWarning(
                "Auth.LoginFailed Reason={Reason} UserId={UserId} ClientApp={ClientApp}",
                "app_access_denied", user.Id, AppAccess.Code(clientApp.Value));
            return AuthOutcome<SessionTokens>.Fail(new AuthFailure(
                AuthFailureKind.Rejected,
                AuthErrorCodes.AppAccessDenied,
                "This account may not use this app.",
                DeniedActor: new DeniedActor(user.Id, AppAccess.AuditSourceFor(clientApp.Value), requiredPermission)));
        }

        var refresh = _refreshTokens.Create();
        var session = AuthSession.Start(
            user.Id, clientApp.Value, platform!.Value, refresh.Hash, now, now + AuthSessionPolicy.SessionLifetime);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // Transparent rehash (R33): same password, fresh hash; not a security-state change.
            await _rehash.ReplaceHashAsync(user.Id, user.PasswordHash, _hasher.Hash(password), cancellationToken);
        }

        _sessions.Add(session);
        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);

        var accessToken = _accessTokens.Issue(
            new AccessTokenSubject(user.Id, session.Id, clientApp.Value, user.SecurityVersion, access.EffectivePermissions, access.ActiveRoleCodes),
            now);

        _logger.LogInformation(
            "Auth.LoginSucceeded UserId={UserId} SessionId={SessionId} ClientApp={ClientApp} Platform={Platform} Rehashed={Rehashed}",
            user.Id, session.Id, AppAccess.Code(clientApp.Value), platform.Value, verification == PasswordVerificationResult.SuccessRehashNeeded);

        return AuthOutcome<SessionTokens>.Success(new SessionTokens(
            accessToken, refresh.Token, session.AbsoluteExpiresAt, platform.Value, user.MustChangePassword));
    }

    private AuthOutcome<SessionTokens> InvalidCredentials(string reason, string? identifierKeyHex, Guid? userId)
    {
        _logger.LogInformation(
            "Auth.LoginFailed Reason={Reason} IdentifierKey={IdentifierKey} UserId={UserId}", reason, identifierKeyHex, userId);
        return AuthOutcome<SessionTokens>.Fail(AuthFailure.Rejected(AuthErrorCodes.InvalidCredentials, "Invalid credentials."));
    }
}
