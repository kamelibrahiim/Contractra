using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// MakeUserPartyLinkOnePerParty (owner decision R30): creates the unique index
/// before dropping the old one, fails without changing anything when a party is
/// already linked from several users, and is reversible. Own database.
/// </summary>
public class UserPartyLinkOnePerPartyMigrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string PreviousMigration = "20261014233000_AddUserCredentialState";
    private const string ThisMigration = "20261014233100_MakeUserPartyLinkOnePerParty";

    /// <summary>
    /// R30 pre-check (ADR 0003 §11): must return no rows on a shared database
    /// before the migration is applied there.
    /// </summary>
    internal const string PreCheckQuery =
        "SELECT party_id, COUNT(*) FROM user_party_links GROUP BY party_id HAVING COUNT(*) > 1;";

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserPartyLinkOnePerPartyMigrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Up_FailsWithoutChanges_WhenAPartyHasTwoLinks_ThenSucceedsOnceResolved_AndDownRestoresTheOldIndex()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ix_user_party_links_party_id';"));

        var party = new Party("م. طرف بحسابين", PartyStatus.Active);
        var firstUser = TestUsers.NewUser("OnePerParty.First");
        var secondUser = TestUsers.NewUser("OnePerParty.Second");
        await using (var seedContext = _fixture.CreateDbContext())
        {
            seedContext.Parties.Add(party);
            seedContext.Users.AddRange(firstUser, secondUser);
            await seedContext.SaveChangesAsync();
        }

        const string insertLink = "INSERT INTO user_party_links (id, user_id, party_id) VALUES (@id, @user_id, @party_id);";
        var secondLinkId = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(connection, null, insertLink, ("id", Guid.CreateVersion7()), ("user_id", firstUser.Id), ("party_id", party.Id));
        await PostgresAssert.ExecuteAsync(connection, null, insertLink, ("id", secondLinkId), ("user_id", secondUser.Id), ("party_id", party.Id));
        Assert.Equal(1, await CountAsync(connection, $"SELECT COUNT(*) FROM ({PreCheckQuery.TrimEnd(';')}) AS duplicates;"));

        await PostgresAssert.FailsWithAsync(() => migrator.MigrateAsync(), "23505");

        Assert.Contains(ThisMigration, await dbContext.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ix_user_party_links_party_id';"));
        Assert.Equal(0, await CountAsync(connection, "SELECT COUNT(*) FROM pg_class WHERE relname = 'ux_user_party_links_party_id';"));
        Assert.Equal(2, await CountAsync(connection, "SELECT COUNT(*) FROM user_party_links;"));

        // Resolving the duplicate is an operational decision made before the
        // migration; this test removes one link only to show the migration then applies.
        await PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM user_party_links WHERE id = @id;", ("id", secondLinkId));
        Assert.Equal(0, await CountAsync(connection, $"SELECT COUNT(*) FROM ({PreCheckQuery.TrimEnd(';')}) AS duplicates;"));

        await migrator.MigrateAsync();
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await CountAsync(connection,
            "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ux_user_party_links_party_id' AND indexdef LIKE 'CREATE UNIQUE INDEX%';"));
        Assert.Equal(0, await CountAsync(connection, "SELECT COUNT(*) FROM pg_class WHERE relname = 'ix_user_party_links_party_id';"));

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(1, await CountAsync(connection,
            "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ix_user_party_links_party_id' AND indexdef LIKE 'CREATE INDEX%';"));
        Assert.Equal(0, await CountAsync(connection, "SELECT COUNT(*) FROM pg_class WHERE relname = 'ux_user_party_links_party_id';"));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM user_party_links;"));

        await migrator.MigrateAsync();
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
    }

    private static Task<long> CountAsync(NpgsqlConnection connection, string sql) =>
        PostgresAssert.CountAsync(connection, null, sql);
}
