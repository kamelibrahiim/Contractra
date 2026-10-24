namespace Contractra.Application.Identity.Auth;

/// <summary>
/// Session lifetimes of contract §3.1, §3.3, §3.6 (owner decisions R5, R17, R21,
/// R35; engineering decision E15). The idle timeouts are configuration
/// (<c>Auth:IdleTimeout:&lt;RoleCode&gt;</c>) bounded to 5 minutes … 24 hours.
/// </summary>
public sealed class AuthSessionPolicy
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(30);

    /// <summary>For a user with no active role, and for a role without a configured value (contract §3.6).</summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(60);

    public static readonly TimeSpan MinIdleTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxIdleTimeout = TimeSpan.FromHours(24);

    /// <summary>The contract defaults: Engineer 8 hours; Accountant, Admin, OfficeStaff 60 minutes.</summary>
    public static IReadOnlyDictionary<string, TimeSpan> DefaultIdleTimeouts { get; } = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
    {
        ["Engineer"] = TimeSpan.FromHours(8),
        ["Accountant"] = TimeSpan.FromMinutes(60),
        ["Admin"] = TimeSpan.FromMinutes(60),
        ["OfficeStaff"] = TimeSpan.FromMinutes(60),
    };

    private readonly IReadOnlyDictionary<string, TimeSpan> _idleTimeouts;

    /// <param name="idleTimeouts">Per role code; each must be within 5 minutes … 24 hours (startup fails otherwise).</param>
    public AuthSessionPolicy(IReadOnlyDictionary<string, TimeSpan> idleTimeouts)
    {
        ArgumentNullException.ThrowIfNull(idleTimeouts);
        foreach (var (role, timeout) in idleTimeouts)
        {
            if (timeout < MinIdleTimeout || timeout > MaxIdleTimeout)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(idleTimeouts), timeout, $"The idle timeout of role '{role}' must be between 5 minutes and 24 hours (contract §3.1).");
            }
        }

        _idleTimeouts = new Dictionary<string, TimeSpan>(idleTimeouts, StringComparer.OrdinalIgnoreCase);
    }

    public static AuthSessionPolicy Default { get; } = new(DefaultIdleTimeouts);

    /// <summary>The shortest idle timeout among the active roles; 60 minutes without an active role (contract §3.6).</summary>
    public TimeSpan IdleTimeoutFor(IEnumerable<string> activeRoleCodes)
    {
        var timeouts = activeRoleCodes
            .Select(code => _idleTimeouts.TryGetValue(code, out var t) ? t : DefaultIdleTimeout)
            .ToList();
        return timeouts.Count == 0 ? DefaultIdleTimeout : timeouts.Min();
    }
}
