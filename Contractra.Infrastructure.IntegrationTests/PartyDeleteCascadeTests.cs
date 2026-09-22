using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

public class PartyDeleteCascadeTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PartyDeleteCascadeTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HardDeleteCascadeBehavior_IsAbsent()
    {
        var party = new Party("Party With Children", PartyStatus.Active);
        party.AssignRole(new PartyRoleCode("Engineer"), new DateOnly(2026, 1, 1));
        party.AddAlias("An Alias");
        party.AddIdentifier(new PartyIdentifierType("NationalId"), "29001011234567");

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Parties.Add(party);
        dbContext.PartyRoles.AddRange(party.Roles);
        dbContext.PartyAliases.AddRange(party.Aliases);
        dbContext.PartyIdentifiers.AddRange(party.Identifiers);
        await dbContext.SaveChangesAsync();

        // No domain method exposes deletion. This proves, at the persistence
        // level, that removing a party row while children still reference it
        // is rejected by the database (ON DELETE RESTRICT / NO ACTION) rather
        // than silently cascading.
        await using var deleteContext = _fixture.CreateDbContext();
        var trackedParty = await deleteContext.Parties.FindAsync(party.Id);
        deleteContext.Parties.Remove(trackedParty!);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => deleteContext.SaveChangesAsync());
        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23001", ((PostgresException)exception.InnerException!).SqlState); // restrict_violation (ON DELETE RESTRICT)

        await using var verifyContext = _fixture.CreateDbContext();
        Assert.True(await verifyContext.Parties.AnyAsync(p => p.Id == party.Id));
        Assert.True(await verifyContext.PartyRoles.AnyAsync(r => r.PartyId == party.Id));
        Assert.True(await verifyContext.PartyAliases.AnyAsync(a => a.PartyId == party.Id));
        Assert.True(await verifyContext.PartyIdentifiers.AnyAsync(i => i.PartyId == party.Id));
    }
}
