namespace Contractra.Infrastructure.Persistence.Throttling;

/// <summary>
/// The persisted throttle layers of auth contract v1.5 §17 (layers 1 and 5 are
/// in-memory and not stored). What each layer counts and when it blocks is the
/// Slice 6 policy (contract §22, pending review), not decided here.
/// </summary>
public static class AuthThrottleScopes
{
    /// <summary>Layer 2: per (identifier key, client source).</summary>
    public const string LoginPair = "login_pair";

    /// <summary>Layer 3: per identifier key, all sources.</summary>
    public const string Identifier = "identifier";

    /// <summary>Layer 4: per user, wrong current password on change-password.</summary>
    public const string ChangePasswordUser = "change_password_user";

    public static readonly IReadOnlyList<string> All = new[] { LoginPair, Identifier, ChangePasswordUser };
}
