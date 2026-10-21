using Contractra.Application.Common;

namespace Contractra.Application.Identity.Users;

/// <summary>
/// The user object of contract §9.3 (and of the §9.1, §9.4, §9.5 responses, read after the
/// change committed): values come from the database at request time.
/// </summary>
public sealed class GetUserQuery
{
    private readonly IUserDirectory _directory;
    private readonly IClock _clock;

    public GetUserQuery(IUserDirectory directory, IClock clock)
    {
        _directory = directory;
        _clock = clock;
    }

    /// <returns>Null when the user does not exist.</returns>
    public async Task<UserSummary?> ExecuteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var entry = await _directory.FindAsync(userId, now, cancellationToken);
        return entry is null ? null : CredentialStateRule.ToSummary(entry, now);
    }
}
