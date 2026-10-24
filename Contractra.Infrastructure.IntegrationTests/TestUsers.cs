using Contractra.Domain.Identity;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Builders for users in integration tests. The hash is an opaque test value:
/// persistence stores it as given and never verifies it.
/// </summary>
internal static class TestUsers
{
    public const string Hash = "AQAAAAIAAYagAAAAEIntegrationTestHash==";

    /// <summary>
    /// Column list and values for a raw-SQL users insert that satisfies every
    /// NOT NULL credential column, for tests that bypass EF Core on purpose.
    /// </summary>
    public const string RawInsertColumns = "id, login_identifier, status, password_hash, must_change_password, security_version";

    public static User NewUser(string loginIdentifier, UserStatus status = UserStatus.Active) =>
        new(new LoginIdentifier(loginIdentifier), status, Hash, mustChangePassword: false);
}
