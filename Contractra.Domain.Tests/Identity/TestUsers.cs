using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

/// <summary>
/// Builders for users in Domain tests. The hash strings are opaque test values:
/// the Domain never hashes or verifies passwords, it only stores the string.
/// </summary>
internal static class TestUsers
{
    public const string Hash = "AQAAAAIAAYagAAAAEDomainTestHash==";
    public const string OtherHash = "AQAAAAIAAYagAAAAEOtherDomainTestHash==";

    public static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    public static User Active(string login = "Test.User01") =>
        new(new LoginIdentifier(login), UserStatus.Active, Hash, mustChangePassword: false);

    public static User WithStatus(UserStatus status, string login = "Test.User01") =>
        new(new LoginIdentifier(login), status, Hash, mustChangePassword: false);

    public static User WithTemporaryPassword(string login = "Test.NewUser01") =>
        new(new LoginIdentifier(login), UserStatus.Active, Hash, mustChangePassword: true, temporaryPasswordExpiresAt: T0.AddHours(72));

    public static Role AdminRole() => new(new RoleCode("Admin"), RoleStatus.Active);

    public static Permission PayrollView() => new(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active);

    public static User BootstrapAdmin() =>
        User.CreateBootstrapAdmin(new LoginIdentifier("bootstrap.admin"), Hash, AdminRole(), PayrollView(), T0, T0);
}
