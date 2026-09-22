using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

public class PartyAliasAndNameTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PartyAliasAndNameTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TwoParties_MayShare_TheSameNormalizedName()
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Parties.Add(new Party("Mohamed Saleem", PartyStatus.Active));
        dbContext.Parties.Add(new Party("Mohamed Saleem", PartyStatus.Active));

        await dbContext.SaveChangesAsync(); // must not throw - BR-168, no unique constraint on normalized_name
    }

    [Fact]
    public async Task TwoParties_MayShare_TheSameAliasText()
    {
        var partyA = new Party("Party A", PartyStatus.Active);
        var partyB = new Party("Party B", PartyStatus.Active);
        partyA.AddAlias("Jamal Abdelshafy  Supply");
        partyB.AddAlias("Jamal Abdelshafy  Supply");

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Parties.AddRange(partyA, partyB);
        dbContext.PartyAliases.AddRange(partyA.Aliases.Concat(partyB.Aliases));

        await dbContext.SaveChangesAsync(); // must not throw - alias uniqueness is scoped to (party_id, alias)
    }

    [Fact]
    public async Task SameParty_CannotContainTheExactSameAlias_Twice()
    {
        var party = new Party("Repeated Alias Party", PartyStatus.Active);

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Parties.Add(party);
        dbContext.PartyAliases.Add(new PartyAlias(party.Id, "Duplicate Alias"));
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.PartyAliases.Add(new PartyAlias(party.Id, "Duplicate Alias"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext2.SaveChangesAsync());
        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", ((PostgresException)exception.InnerException!).SqlState);
    }
}
