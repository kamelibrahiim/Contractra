using Contractra.Application.Common;

namespace Contractra.Application.Identity.Auth;

public sealed record CurrentUser(
    Guid Id,
    string LoginIdentifier,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

/// <summary>
/// <c>GET /api/v1/auth/me</c> (auth contract v1.5 §5.4): values come from the database
/// at request time, not from the token's claims. Permissions are the effective
/// permissions sorted by code; roles the active role codes.
/// </summary>
public sealed class CurrentUserQuery
{
    private readonly IUserRepository _users;
    private readonly IUserAccessReader _access;
    private readonly IClock _clock;

    public CurrentUserQuery(IUserRepository users, IUserAccessReader access, IClock clock)
    {
        _users = users;
        _access = access;
        _clock = clock;
    }

    /// <returns>Null when the user no longer exists.</returns>
    public async Task<CurrentUser?> ExecuteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var access = await _access.GetAsync(userId, _clock.UtcNow, cancellationToken);
        return new CurrentUser(
            user.Id,
            user.LoginIdentifier.Value,
            access.ActiveRoleCodes.Order(StringComparer.Ordinal).ToList(),
            access.EffectivePermissions.Order(StringComparer.Ordinal).ToList(),
            user.MustChangePassword);
    }
}
