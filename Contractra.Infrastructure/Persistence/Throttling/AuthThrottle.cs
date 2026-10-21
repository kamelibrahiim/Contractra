using System.Security.Cryptography;
using System.Text;
using Contractra.Application.Identity.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Infrastructure.Persistence.Throttling;

/// <summary>
/// Contract v1.5 §17 layers 2-4 over the shared <see cref="AuthThrottleStore"/>.
/// Each step runs in its own DI scope, so its short transaction never joins a use
/// case's. Layers 2 and 3 are decided together on both locked rows: an attempt is
/// admitted only if both layers admit it, and a rejected attempt changes nothing. If
/// the store cannot be reached the admission is <see cref="ThrottleStatus.Unavailable"/>
/// and the caller fails closed. Releasing a success is best effort: the attempt was
/// already counted, so a failure there only makes the throttle stricter.
/// </summary>
public sealed class AuthThrottle : IAuthThrottle
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AuthThrottleOptions _options;
    private readonly ILogger<AuthThrottle> _logger;

    public AuthThrottle(IServiceScopeFactory scopes, AuthThrottleOptions options, ILogger<AuthThrottle>? logger = null)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger ?? NullLogger<AuthThrottle>.Instance;
    }

    /// <summary>The pair key of layer 2: SHA-256 over the identifier key and the client source.</summary>
    public static byte[] PairKey(byte[] identifierKey, string clientSource)
    {
        ArgumentNullException.ThrowIfNull(identifierKey);
        ArgumentNullException.ThrowIfNull(clientSource);
        var source = Encoding.UTF8.GetBytes(clientSource);
        var input = new byte[identifierKey.Length + 1 + source.Length];
        identifierKey.CopyTo(input, 0);
        source.CopyTo(input, identifierKey.Length + 1);
        return SHA256.HashData(input);
    }

    /// <summary>The user key of layer 4.</summary>
    public static byte[] UserKey(Guid userId) => SHA256.HashData(userId.ToByteArray());

    public async Task<ThrottleAdmission> AdmitCredentialAttemptAsync(
        byte[] identifierKey, string clientSource, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        now = ToStoredPrecision(now);
        var pair = new AuthThrottleKey(AuthThrottleScopes.LoginPair, PairKey(identifierKey, clientSource));
        var identifier = new AuthThrottleKey(AuthThrottleScopes.Identifier, identifierKey);

        try
        {
            var outcome = await ApplyAsync([pair, identifier], states =>
            {
                var pairStep = AuthThrottlePolicy.AdmitPair(states[pair], now, _options);
                var identifierStep = AuthThrottlePolicy.AdmitIdentifier(states[identifier], now, _options);
                if (!pairStep.Admitted || !identifierStep.Admitted)
                {
                    var retry = TimeSpan.FromTicks(Math.Max(
                        pairStep.Admitted ? 0 : pairStep.RetryAfter.Ticks,
                        identifierStep.Admitted ? 0 : identifierStep.RetryAfter.Ticks));
                    var layer = !pairStep.Admitted && !identifierStep.Admitted ? "2,3" : !pairStep.Admitted ? "2" : "3";
                    return new AuthThrottleDecision<CredentialOutcome>(new CredentialOutcome(false, retry, layer, false));
                }

                return new AuthThrottleDecision<CredentialOutcome>(
                    new CredentialOutcome(true, TimeSpan.Zero, null, identifierStep.EnteredSlowMode),
                    new Dictionary<AuthThrottleKey, AuthThrottleState?> { [pair] = pairStep.Next, [identifier] = identifierStep.Next });
            }, cancellationToken);

            var identifierKeyHex = ThrottleKeys.ToHex(identifierKey);
            if (!outcome.Admitted)
            {
                _logger.LogInformation(
                    "Auth.Throttled Layer={Layer} IdentifierKey={IdentifierKey} ClientIp={ClientIp} RetryAfterSeconds={RetryAfter}",
                    outcome.Layer, identifierKeyHex, clientSource, Math.Ceiling(outcome.RetryAfter.TotalSeconds));
                return new ThrottleAdmission(ThrottleStatus.Throttled, outcome.RetryAfter);
            }

            if (outcome.EnteredSlowMode)
            {
                _logger.LogWarning(
                    "Auth.Throttled Layer={Layer} Activated=true IdentifierKey={IdentifierKey} ClientIp={ClientIp}: the identifier is in slow mode (possible attack).",
                    "3", identifierKeyHex, clientSource);
            }

            return new ThrottleAdmission(ThrottleStatus.Admitted, Ticket: new ThrottleTicket([pair.KeyHash.ToArray(), identifierKey], now, outcome.EnteredSlowMode));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "Auth.ThrottleStoreUnavailable Layer={Layer}: failing closed.", "2,3");
            return ThrottleAdmission.Unavailable();
        }
    }

    public async Task RecordCredentialSuccessAsync(ThrottleTicket ticket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var pair = new AuthThrottleKey(AuthThrottleScopes.LoginPair, ticket.Keys[0]);
        var identifier = new AuthThrottleKey(AuthThrottleScopes.Identifier, ticket.Keys[1]);

        try
        {
            await ApplyAsync([pair, identifier], states =>
            {
                var changes = new Dictionary<AuthThrottleKey, AuthThrottleState?> { [pair] = null };
                var (changed, next) = AuthThrottlePolicy.ReleaseIdentifier(states[identifier], ticket.AdmittedAt, ticket.EnteredIdentifierSlowMode, _options);
                if (changed)
                {
                    changes[identifier] = next;
                }

                return new AuthThrottleDecision<bool>(true, changes);
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Auth.ThrottleReleaseFailed Layer={Layer}: the attempt stays counted.", "2,3");
        }
    }

    public async Task<ThrottleAdmission> AdmitChangePasswordAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        now = ToStoredPrecision(now);
        var key = new AuthThrottleKey(AuthThrottleScopes.ChangePasswordUser, UserKey(userId));

        try
        {
            var step = await ApplyAsync([key], states =>
            {
                var result = AuthThrottlePolicy.AdmitChangePassword(states[key], now, _options);
                return result.Admitted
                    ? new AuthThrottleDecision<AuthThrottleStep>(result, new Dictionary<AuthThrottleKey, AuthThrottleState?> { [key] = result.Next })
                    : new AuthThrottleDecision<AuthThrottleStep>(result);
            }, cancellationToken);

            if (!step.Admitted)
            {
                _logger.LogInformation(
                    "Auth.Throttled Layer={Layer} UserId={UserId} RetryAfterSeconds={RetryAfter}", "4", userId, Math.Ceiling(step.RetryAfter.TotalSeconds));
                return new ThrottleAdmission(ThrottleStatus.Throttled, step.RetryAfter);
            }

            return new ThrottleAdmission(ThrottleStatus.Admitted, Ticket: new ThrottleTicket([key.KeyHash.ToArray()], now, false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "Auth.ThrottleStoreUnavailable Layer={Layer}: failing closed.", "4");
            return ThrottleAdmission.Unavailable();
        }
    }

    public async Task RecordChangePasswordNotWrongAsync(ThrottleTicket ticket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var key = new AuthThrottleKey(AuthThrottleScopes.ChangePasswordUser, ticket.Keys[0]);

        try
        {
            await ApplyAsync([key], states =>
            {
                var (changed, next) = AuthThrottlePolicy.ReleaseChangePassword(states[key], ticket.AdmittedAt);
                return changed
                    ? new AuthThrottleDecision<bool>(true, new Dictionary<AuthThrottleKey, AuthThrottleState?> { [key] = next })
                    : new AuthThrottleDecision<bool>(false);
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Auth.ThrottleReleaseFailed Layer={Layer}: the attempt stays counted.", "4");
        }
    }

    public async Task ClearIdentifierAsync(byte[] identifierKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifierKey);
        var key = new AuthThrottleKey(AuthThrottleScopes.Identifier, identifierKey);

        await ApplyAsync(
            [key],
            states => new AuthThrottleDecision<bool>(true, new Dictionary<AuthThrottleKey, AuthThrottleState?> { [key] = null }),
            cancellationToken);
    }

    /// <summary>timestamptz keeps microseconds; decisions compare stored and in-memory times exactly.</summary>
    public static DateTimeOffset ToStoredPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);

    private async Task<TResult> ApplyAsync<TResult>(
        IReadOnlyCollection<AuthThrottleKey> keys,
        Func<IReadOnlyDictionary<AuthThrottleKey, AuthThrottleState?>, AuthThrottleDecision<TResult>> decide,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<AuthThrottleStore>();
        return await store.ApplyAsync(keys, decide, _options.LockTimeout, cancellationToken);
    }

    private sealed record CredentialOutcome(bool Admitted, TimeSpan RetryAfter, string? Layer, bool EnteredSlowMode);
}
