using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Queries;

/// <summary>
/// Finds users by login identifier the way the database defines identity:
/// case-insensitively, through the unique expression index
/// ux_users_login_identifier_lower (ADR 0003 §3.2; auth contract v1.5 §5.1,
/// plan E10). An exact EF equality on the value object would miss "kamel" when
/// "Kamel" is stored and could not use that index.
/// </summary>
public static class UserLoginLookup
{
    /// <summary>
    /// The SQL the lookup runs; <c>{0}</c> is always bound as a parameter.
    /// </summary>
    public const string Sql = "SELECT * FROM users WHERE lower(login_identifier) = lower({0})";

    /// <summary>
    /// Users whose identifier equals <paramref name="loginIdentifierInput"/>
    /// ignoring case. The input is not trimmed or normalized, so padded input
    /// matches nobody. The result is composable (Include, AsTracking, …).
    /// </summary>
    public static IQueryable<User> WhereLoginIdentifierMatches(this DbSet<User> users, string loginIdentifierInput)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(loginIdentifierInput);

        return users.FromSqlRaw(Sql, loginIdentifierInput);
    }
}
