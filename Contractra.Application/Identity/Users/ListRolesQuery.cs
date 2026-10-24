namespace Contractra.Application.Identity.Users;

/// <summary><c>GET /api/v1/roles</c> (auth contract v1.5 §9.7): the codes of the Active roles, sorted by code.</summary>
public sealed class ListRolesQuery
{
    private readonly IRoleRepository _roles;

    public ListRolesQuery(IRoleRepository roles)
    {
        _roles = roles;
    }

    public async Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        (await _roles.ListActiveCodesAsync(cancellationToken)).Order(StringComparer.Ordinal).ToList();
}
