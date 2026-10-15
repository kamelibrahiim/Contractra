using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Contractra.Infrastructure.Persistence.Configurations;

/// <summary>
/// The stored codes of the auth enums (auth contract v1.5 §3.3, §4, §5.1;
/// ADR 0005 §3.1). One table per enum feeds both the value converter and the
/// CHECK constraint, so the two cannot drift apart.
/// </summary>
internal static class AuthCodes
{
    public static readonly IReadOnlyDictionary<ClientApp, string> ClientApps = new Dictionary<ClientApp, string>
    {
        [ClientApp.Admin] = "admin",
        [ClientApp.Employee] = "employee",
    };

    public static readonly IReadOnlyDictionary<ClientPlatform, string> ClientPlatforms = new Dictionary<ClientPlatform, string>
    {
        [ClientPlatform.Web] = "web",
        [ClientPlatform.Mobile] = "mobile",
    };

    public static readonly IReadOnlyDictionary<SessionRevokeReason, string> RevokeReasons = new Dictionary<SessionRevokeReason, string>
    {
        [SessionRevokeReason.Logout] = "logout",
        [SessionRevokeReason.Idle] = "idle",
        [SessionRevokeReason.RefreshReuse] = "refresh_reuse",
        [SessionRevokeReason.AppAccessLost] = "app_access_lost",
        [SessionRevokeReason.UserDisabled] = "user_disabled",
        [SessionRevokeReason.PasswordResetIssued] = "password_reset_issued",
        [SessionRevokeReason.PasswordResetCompleted] = "password_reset_completed",
        [SessionRevokeReason.InitialPasswordReissued] = "initial_password_reissued",
        [SessionRevokeReason.PasswordChanged] = "password_changed",
    };

    public static ValueConverter<TEnum, string> Converter<TEnum>(IReadOnlyDictionary<TEnum, string> codes)
        where TEnum : struct, Enum
    {
        var values = codes.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        return new ValueConverter<TEnum, string>(value => codes[value], code => values[code]);
    }

    /// <summary>"column IN ('a', 'b')" for a CHECK constraint.</summary>
    public static string InList<TEnum>(string column, IReadOnlyDictionary<TEnum, string> codes)
        where TEnum : struct, Enum =>
        $"{column} IN ({string.Join(", ", codes.Values.Select(code => $"'{code}'"))})";
}
