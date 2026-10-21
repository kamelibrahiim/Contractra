using Contractra.Application.Common;

namespace Contractra.Application.Identity.Users;

public sealed record UserListResult(IReadOnlyList<UserSummary> Items, int Page, int PageSize, int TotalCount);

/// <summary><c>GET /api/v1/users</c> (auth contract v1.5 §9.2). The caller validates the criteria (page &gt;= 1, 1..<see cref="UserListCriteria.MaxPageSize"/> per page).</summary>
public sealed class ListUsersQuery
{
    private readonly IUserDirectory _directory;
    private readonly IClock _clock;

    public ListUsersQuery(IUserDirectory directory, IClock clock)
    {
        _directory = directory;
        _clock = clock;
    }

    public async Task<UserListResult> ExecuteAsync(UserListCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var now = _clock.UtcNow;
        var page = await _directory.ListAsync(criteria, now, cancellationToken);
        return new UserListResult(
            page.Items.Select(entry => CredentialStateRule.ToSummary(entry, now)).ToList(),
            criteria.Page,
            criteria.PageSize,
            page.TotalCount);
    }
}
