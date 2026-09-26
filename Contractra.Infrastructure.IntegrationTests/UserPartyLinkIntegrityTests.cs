using Contractra.Domain.Identity;
using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

public class UserPartyLinkIntegrityTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string InsertLink =
        "INSERT INTO user_party_links (id, user_id, party_id) VALUES (@id, @user_id, @party_id);";

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserPartyLinkIntegrityTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Link_RoundTripsThroughEfCore()
    {
        var user = await SeedUserAsync();
        var party = await SeedPartyAsync();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            (await dbContext.Users.Include(u => u.PartyLink).SingleAsync(u => u.Id == user.Id)).LinkToParty(party);
            await dbContext.SaveChangesAsync();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var stored = await verifyContext.Users.Include(u => u.PartyLink).SingleAsync(u => u.Id == user.Id);
        Assert.NotNull(stored.PartyLink);
        Assert.Equal(party.Id, stored.PartyLink.PartyId);
    }

    [Fact]
    public async Task UserWithoutALink_LoadsWithANullPartyLink()
    {
        var user = await SeedUserAsync();

        await using var dbContext = _fixture.CreateDbContext();
        Assert.Null((await dbContext.Users.Include(u => u.PartyLink).SingleAsync(u => u.Id == user.Id)).PartyLink);
    }

    [Fact]
    public async Task SecondLinkForTheSameUser_ViolatesTheUniqueIndex()
    {
        var user = await SeedUserAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, (await SeedPartyAsync()).Id);
        var otherParty = await SeedPartyAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, user.Id, otherParty.Id),
            "23505",
            "ux_user_party_links_user_id");
    }

    [Fact]
    public async Task SecondLink_SavedThroughEfCore_IsRejectedByTheDatabase_WhenTheLinkWasNotLoaded()
    {
        var user = await SeedUserAsync();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            (await dbContext.Users.Include(u => u.PartyLink).SingleAsync(u => u.Id == user.Id)).LinkToParty(await SeedPartyAsync());
            await dbContext.SaveChangesAsync();
        }

        // The link is not loaded here, so the domain check cannot see it; the
        // database's UNIQUE(user_id) is the final guard.
        var otherParty = await SeedPartyAsync();
        await using var dbContext2 = _fixture.CreateDbContext();
        (await dbContext2.Users.SingleAsync(u => u.Id == user.Id)).LinkToParty(otherParty);

        await PostgresAssert.FailsWithAsync(() => dbContext2.SaveChangesAsync(), "23505", "ux_user_party_links_user_id");
    }

    [Fact]
    public async Task TwoUsers_MayLinkToTheSameParty()
    {
        // Owner decision (2026-09-26): party_id is not unique, e.g. a disabled
        // account and its replacement both stay linked to the same engineer.
        var party = await SeedPartyAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, (await SeedUserAsync()).Id, party.Id);

        Assert.Equal(1, await InsertAsync(connection, (await SeedUserAsync()).Id, party.Id));
    }

    [Fact]
    public async Task LinkToAMissingUser_ViolatesTheUserForeignKey()
    {
        var party = await SeedPartyAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, Guid.CreateVersion7(), party.Id),
            "23503",
            "fk_user_party_links_users_user_id");
    }

    [Fact]
    public async Task LinkToAMissingParty_ViolatesThePartyForeignKey()
    {
        var user = await SeedUserAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, user.Id, Guid.CreateVersion7()),
            "23503",
            "fk_user_party_links_parties_party_id");
    }

    [Fact]
    public async Task DeletingALinkedUser_IsRestricted()
    {
        var user = await SeedUserAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, (await SeedPartyAsync()).Id);

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM users WHERE id = @id;", ("id", user.Id)),
            "23001", // restrict_violation (ON DELETE RESTRICT)
            "fk_user_party_links_users_user_id");
    }

    [Fact]
    public async Task DeletingALinkedParty_IsRestricted()
    {
        var party = await SeedPartyAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, (await SeedUserAsync()).Id, party.Id);

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM parties WHERE id = @id;", ("id", party.Id)),
            "23001", // restrict_violation (ON DELETE RESTRICT)
            "fk_user_party_links_parties_party_id");
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User(new LoginIdentifier($"PartyLink.{Guid.NewGuid():N}"), UserStatus.Active);
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private async Task<Party> SeedPartyAsync()
    {
        var party = new Party($"م. طرف {Guid.NewGuid():N}", PartyStatus.Active);
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Parties.Add(party);
        await dbContext.SaveChangesAsync();
        return party;
    }

    private static Task<int> InsertAsync(NpgsqlConnection connection, Guid userId, Guid partyId) =>
        PostgresAssert.ExecuteAsync(connection, null, InsertLink,
            ("id", Guid.CreateVersion7()), ("user_id", userId), ("party_id", partyId));

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
