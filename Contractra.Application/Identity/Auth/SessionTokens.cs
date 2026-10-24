using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.Auth;

/// <summary>
/// The tokens login, refresh and change-password return (contract §5.1, §5.2, §5.5).
/// <see cref="RefreshToken"/> is null within the 30-second reuse grace (§5.2), when
/// no new refresh token is issued. The HTTP layer delivers it in the body (mobile) or
/// in the cookie (web) according to the session's stored <see cref="Platform"/>.
/// </summary>
public sealed record SessionTokens(
    IssuedAccessToken AccessToken,
    string? RefreshToken,
    DateTimeOffset SessionAbsoluteExpiresAt,
    ClientPlatform Platform,
    bool MustChangePassword);

public static class AppAccess
{
    public const string AdminPermission = "Apps.Admin.Access";
    public const string EmployeePermission = "Apps.Employee.Access";

    /// <summary>The app-access permission of a client app (contract §2, owner decision R13).</summary>
    public static string PermissionFor(ClientApp app) => app switch
    {
        ClientApp.Admin => AdminPermission,
        ClientApp.Employee => EmployeePermission,
        _ => throw new ArgumentOutOfRangeException(nameof(app), app, "Unknown client app."),
    };

    /// <summary>Audit source of an app (contract §14.1, owner decision R46): Admin → Web, Employee → EmployeeApp.</summary>
    public static AuditSource AuditSourceFor(ClientApp app) => app switch
    {
        ClientApp.Admin => AuditSource.Web,
        ClientApp.Employee => AuditSource.EmployeeApp,
        _ => throw new ArgumentOutOfRangeException(nameof(app), app, "Unknown client app."),
    };

    public static ClientApp? Parse(string? value) => value switch
    {
        "admin" => ClientApp.Admin,
        "employee" => ClientApp.Employee,
        _ => null,
    };

    public static string Code(ClientApp app) => app switch
    {
        ClientApp.Admin => "admin",
        ClientApp.Employee => "employee",
        _ => throw new ArgumentOutOfRangeException(nameof(app), app, "Unknown client app."),
    };
}
