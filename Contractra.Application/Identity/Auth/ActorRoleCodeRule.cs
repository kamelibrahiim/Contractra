namespace Contractra.Application.Identity.Auth;

/// <summary>
/// The deterministic actor_role_code rule of contract §14.1, confirmed by owner
/// decision OC-6 (contract §19): the active role through which the actor held the
/// permission the action required (several - the first by ordinal code order); if
/// that permission came only from a direct grant, or the action needs no permission,
/// the actor's first active role code by the same order; null if the actor has no
/// active role.
/// </summary>
public static class ActorRoleCodeRule
{
    public static string? Resolve(UserAccess access, string? requiredPermission)
    {
        ArgumentNullException.ThrowIfNull(access);

        var activeRoles = access.ActiveRoleCodes.Order(StringComparer.Ordinal).ToList();
        if (activeRoles.Count == 0)
        {
            return null;
        }

        if (requiredPermission is not null)
        {
            var granting = activeRoles.FirstOrDefault(role =>
                access.PermissionsByActiveRole.TryGetValue(role, out var permissions)
                && permissions.Contains(requiredPermission, StringComparer.Ordinal));
            if (granting is not null)
            {
                return granting;
            }
        }

        return activeRoles[0];
    }
}
