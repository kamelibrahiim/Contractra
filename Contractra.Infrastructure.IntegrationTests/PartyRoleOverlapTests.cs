using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

public class PartyRoleOverlapTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PartyRoleOverlapTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OverlappingPeriod_SamePartySameRole_IsRejected()
    {
        var partyId = Guid.CreateVersion7();
        var roleCode = new PartyRoleCode("Engineer");

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Parties.Add(new Party(partyId, "Overlap Test Party", PartyStatus.Active));
        dbContext.PartyRoles.Add(new PartyRole(partyId, roleCode, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.PartyRoles.Add(new PartyRole(partyId, roleCode, new DateOnly(2026, 3, 1), new DateOnly(2026, 9, 30)));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext2.SaveChangesAsync());
        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23P01", ((PostgresException)exception.InnerException!).SqlState); // exclusion_violation
    }

    [Fact]
    public async Task TouchingInclusivePeriods_OnTheSameDate_AreRejected()
    {
        var partyId = Guid.CreateVersion7();
        var roleCode = new PartyRoleCode("Supplier");

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Parties.Add(new Party(partyId, "Touching Boundary Party", PartyStatus.Active));
        dbContext.PartyRoles.Add(new PartyRole(partyId, roleCode, new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 30)));
        await dbContext.SaveChangesAsync();

        // Starts exactly on the day the previous period ends - inclusive bounds mean this touches.
        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.PartyRoles.Add(new PartyRole(partyId, roleCode, new DateOnly(2026, 9, 30), new DateOnly(2026, 12, 31)));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext2.SaveChangesAsync());
    }

    [Fact]
    public async Task NewPeriod_BeginningTheDayAfterEffectiveTo_Succeeds()
    {
        var partyId = Guid.CreateVersion7();
        var roleCode = new PartyRoleCode("Contractor");

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Parties.Add(new Party(partyId, "Sequential Period Party", PartyStatus.Active));
        dbContext.PartyRoles.Add(new PartyRole(partyId, roleCode, new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 30)));
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.PartyRoles.Add(new PartyRole(partyId, roleCode, new DateOnly(2026, 10, 1), null));

        await dbContext2.SaveChangesAsync(); // must not throw

        await using var verifyContext = _fixture.CreateDbContext();
        var roleCount = await verifyContext.PartyRoles.CountAsync(r => r.PartyId == partyId);
        Assert.Equal(2, roleCount);
    }

    [Fact]
    public async Task DifferentRoleCode_MayOverlap_ForTheSameParty()
    {
        var partyId = Guid.CreateVersion7();

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Parties.Add(new Party(partyId, "Multi-Role Party", PartyStatus.Active));
        dbContext.PartyRoles.Add(new PartyRole(partyId, new PartyRoleCode("Supplier"), new DateOnly(2026, 1, 1), null));
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.PartyRoles.Add(new PartyRole(partyId, new PartyRoleCode("Contractor"), new DateOnly(2026, 1, 1), null));

        await dbContext2.SaveChangesAsync(); // must not throw - different role_code
    }

    [Fact]
    public async Task DifferentParty_MayUseTheSameRolePeriod()
    {
        var partyAId = Guid.CreateVersion7();
        var partyBId = Guid.CreateVersion7();
        var roleCode = new PartyRoleCode("Engineer");

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Parties.Add(new Party(partyAId, "Party A", PartyStatus.Active));
        dbContext.Parties.Add(new Party(partyBId, "Party B", PartyStatus.Active));
        dbContext.PartyRoles.Add(new PartyRole(partyAId, roleCode, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.PartyRoles.Add(new PartyRole(partyBId, roleCode, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));

        await dbContext2.SaveChangesAsync(); // must not throw - different party_id
    }
}
