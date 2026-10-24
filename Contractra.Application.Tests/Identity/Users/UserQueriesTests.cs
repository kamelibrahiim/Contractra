using Contractra.Application.Identity.Users;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Users;

/// <summary>Auth contract v1.5 §9.2, §9.3, §9.7: the read side of the Admin users endpoints.</summary>
public class UserQueriesTests
{
    private readonly FakeClock _clock = new();
    private readonly FakeUserDirectory _directory = new();

    private sealed class FakeUserDirectory : IUserDirectory
    {
        public List<UserDirectoryEntry> Entries { get; } = [];

        public DateTimeOffset? LastAsOf { get; private set; }

        public UserListCriteria? LastCriteria { get; private set; }

        public Task<UserDirectoryPage> ListAsync(UserListCriteria criteria, DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            LastCriteria = criteria;
            LastAsOf = asOf;
            return Task.FromResult(new UserDirectoryPage(Entries, Entries.Count + 40));
        }

        public Task<UserDirectoryEntry?> FindAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            LastAsOf = asOf;
            return Task.FromResult(Entries.SingleOrDefault(e => e.Id == userId));
        }
    }

    private UserDirectoryEntry Add(string login, bool mustChange = false, DateTimeOffset? tempExpiry = null)
    {
        var entry = new UserDirectoryEntry(Guid.CreateVersion7(), login, UserStatus.Active, false, ["Engineer"], mustChange, tempExpiry, true, null);
        _directory.Entries.Add(entry);
        return entry;
    }

    [Fact]
    public async Task GetUser_ReturnsTheSummaryWithTheStateDerivedAtTheClockTime()
    {
        var entry = Add("eng.ahmed", mustChange: true, tempExpiry: _clock.UtcNow.AddHours(1));
        var query = new GetUserQuery(_directory, _clock);

        var summary = await query.ExecuteAsync(entry.Id);

        Assert.NotNull(summary);
        Assert.Equal(CredentialState.TemporaryPasswordIssued, summary.CredentialState);
        _clock.UtcNow = _clock.UtcNow.AddHours(2);
        Assert.Equal(CredentialState.InitialPasswordRequired, (await query.ExecuteAsync(entry.Id))!.CredentialState);
        Assert.Equal(_clock.UtcNow, _directory.LastAsOf);
    }

    [Fact]
    public async Task GetUser_ReturnsNull_ForAnUnknownUser() =>
        Assert.Null(await new GetUserQuery(_directory, _clock).ExecuteAsync(Guid.CreateVersion7()));

    [Fact]
    public async Task ListUsers_ReturnsThePageWithTheCriteriaAndTheTotalCount()
    {
        Add("a");
        Add("b");
        var criteria = new UserListCriteria(3, 2, "e", UserStatus.Active);

        var result = await new ListUsersQuery(_directory, _clock).ExecuteAsync(criteria);

        Assert.Equal(["a", "b"], result.Items.Select(i => i.LoginIdentifier));
        Assert.Equal(3, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(42, result.TotalCount);
        Assert.Same(criteria, _directory.LastCriteria);
        Assert.All(result.Items, i => Assert.Equal(CredentialState.PasswordSet, i.CredentialState));
    }

    [Fact]
    public async Task ListRoles_ReturnsOnlyActiveRoleCodes_SortedByCode()
    {
        var roles = new FakeRoleRepository();
        roles.Seed(new Role(new RoleCode("OfficeStaff"), RoleStatus.Active));
        roles.Seed(new Role(new RoleCode("Legacy"), RoleStatus.Deprecated));
        roles.Seed(new Role(new RoleCode("Admin"), RoleStatus.Active));
        roles.Seed(new Role(new RoleCode("Engineer"), RoleStatus.Active));

        var codes = await new ListRolesQuery(roles).ExecuteAsync();

        Assert.Equal(["Admin", "Engineer", "OfficeStaff"], codes);
    }
}
