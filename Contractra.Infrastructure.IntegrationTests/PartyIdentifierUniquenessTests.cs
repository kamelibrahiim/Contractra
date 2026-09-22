using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

public class PartyIdentifierUniquenessTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PartyIdentifierUniquenessTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DuplicateCurrentIdentifier_SameFullScope_IsRejected()
    {
        var type = new PartyIdentifierType("CommercialRegistration");

        await using var dbContext = _fixture.CreateDbContext();
        var partyA = new Party("Party A", PartyStatus.Active);
        var partyB = new Party("Party B", PartyStatus.Active);
        dbContext.Parties.AddRange(partyA, partyB);
        partyA.AddIdentifier(type, "3660", jurisdictionCountry: "EG", issuingAuthority: "GAFI");
        dbContext.PartyIdentifiers.AddRange(partyA.Identifiers);
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.PartyIdentifiers.Add(new PartyIdentifier(
            partyB.Id, type, "3660", jurisdictionCountry: "EG", issuingAuthority: "GAFI"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext2.SaveChangesAsync());
        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", ((PostgresException)exception.InnerException!).SqlState); // unique_violation
    }

    [Fact]
    public async Task DuplicateCurrentIdentifier_WithNullIssuingAuthority_IsRejected()
    {
        var type = new PartyIdentifierType("NationalId");

        await using var dbContext = _fixture.CreateDbContext();
        var partyA = new Party("Party A", PartyStatus.Active);
        var partyB = new Party("Party B", PartyStatus.Active);
        dbContext.Parties.AddRange(partyA, partyB);
        partyA.AddIdentifier(type, "29001011234567", jurisdictionCountry: "EG", issuingAuthority: null);
        dbContext.PartyIdentifiers.AddRange(partyA.Identifiers);
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.PartyIdentifiers.Add(new PartyIdentifier(
            partyB.Id, type, "29001011234567", jurisdictionCountry: "EG", issuingAuthority: null));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext2.SaveChangesAsync());
        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", ((PostgresException)exception.InnerException!).SqlState);
    }

    [Fact]
    public async Task DuplicateCurrentIdentifier_WithNullJurisdictionCountry_IsRejected()
    {
        var type = new PartyIdentifierType("TaxCard");

        await using var dbContext = _fixture.CreateDbContext();
        var partyA = new Party("Party A", PartyStatus.Active);
        var partyB = new Party("Party B", PartyStatus.Active);
        dbContext.Parties.AddRange(partyA, partyB);
        partyA.AddIdentifier(type, "855-951-320", jurisdictionCountry: null, issuingAuthority: "Tax Authority");
        dbContext.PartyIdentifiers.AddRange(partyA.Identifiers);
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.PartyIdentifiers.Add(new PartyIdentifier(
            partyB.Id, type, "855-951-320", jurisdictionCountry: null, issuingAuthority: "Tax Authority"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext2.SaveChangesAsync());
        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", ((PostgresException)exception.InnerException!).SqlState);
    }

    [Fact]
    public async Task ClosedHistoricalIdentifier_DoesNotBlock_ANewCurrentIdentifier()
    {
        var type = new PartyIdentifierType("CommercialRegistration");

        await using var dbContext = _fixture.CreateDbContext();
        var partyA = new Party("Party A", PartyStatus.Active);
        var partyB = new Party("Party B", PartyStatus.Active);
        dbContext.Parties.AddRange(partyA, partyB);

        // Created current, then explicitly closed as of a fixed date - must not
        // participate in the current-scope unique index once closed.
        var identifierA = partyA.AddIdentifier(type, "309429-CLOSED", "EG", "GAFI");
        identifierA.Close(closedOn: new DateOnly(2025, 1, 1), asOf: new DateOnly(2025, 1, 1));
        dbContext.PartyIdentifiers.AddRange(partyA.Identifiers);
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.PartyIdentifiers.Add(new PartyIdentifier(
            partyB.Id, type, "309429-CLOSED", jurisdictionCountry: "EG", issuingAuthority: "GAFI"));

        await dbContext2.SaveChangesAsync(); // must not throw
    }
}
