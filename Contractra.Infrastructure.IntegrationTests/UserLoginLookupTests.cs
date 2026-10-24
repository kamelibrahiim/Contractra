using Contractra.Infrastructure.Persistence.Queries;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Case-insensitive login-identifier lookup (ADR 0003 §3.2; auth contract v1.5
/// §5.1; plan E10).
/// </summary>
public class UserLoginLookupTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public UserLoginLookupTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("Lookup.Kamel")]
    [InlineData("lookup.kamel")]
    [InlineData("LOOKUP.KAMEL")]
    [InlineData("lOOkUp.kAmEl")]
    public async Task MixedCaseInput_FindsTheUser_AndReturnsTheStoredCasing(string input)
    {
        var user = await EnsureUserAsync("Lookup.Kamel");

        await using var dbContext = _fixture.CreateDbContext();
        var found = await dbContext.Users.WhereLoginIdentifierMatches(input).SingleOrDefaultAsync();

        Assert.NotNull(found);
        Assert.Equal(user.Id, found.Id);
        Assert.Equal("Lookup.Kamel", found.LoginIdentifier.Value);
    }

    [Theory]
    [InlineData(" Lookup.Kamel")]
    [InlineData("Lookup.Kamel ")]
    [InlineData("Lookup.Kamel2")]
    [InlineData("")]
    public async Task PaddedOrDifferentInput_FindsNobody(string input)
    {
        await EnsureUserAsync("Lookup.Kamel");

        await using var dbContext = _fixture.CreateDbContext();

        Assert.Null(await dbContext.Users.WhereLoginIdentifierMatches(input).SingleOrDefaultAsync());
    }

    [Fact]
    public async Task Input_IsAParameter_NotSql()
    {
        await EnsureUserAsync("Lookup.Kamel");

        await using var dbContext = _fixture.CreateDbContext();

        Assert.Empty(await dbContext.Users.WhereLoginIdentifierMatches("x' OR '1'='1").ToListAsync());
    }

    [Fact]
    public async Task TheLookup_IsComposable_SoRolesAndPermissionsCanBeLoadedWithIt()
    {
        await EnsureUserAsync("Lookup.Kamel");

        await using var dbContext = _fixture.CreateDbContext();
        var found = await dbContext.Users
            .WhereLoginIdentifierMatches("LOOKUP.kamel")
            .Include(u => u.Roles)
            .Include(u => u.Permissions)
            .SingleAsync();

        Assert.Empty(found.Roles);
    }

    [Fact]
    public async Task TheLookup_CanUseTheCaseInsensitiveUniqueIndex()
    {
        await EnsureUserAsync("Lookup.Kamel");
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await PostgresAssert.ExecuteAsync(connection, null, "SET enable_seqscan = off;");

        await using var command = new NpgsqlCommand(
            $"EXPLAIN {UserLoginLookup.Sql.Replace("{0}", "@p0")}", connection);
        command.Parameters.AddWithValue("p0", "lookup.kamel");
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        Assert.Contains(plan, line => line.Contains("ux_users_login_identifier_lower"));
    }

    private async Task<Domain.Identity.User> EnsureUserAsync(string login)
    {
        await using var dbContext = _fixture.CreateDbContext();
        var existing = await dbContext.Users.SingleOrDefaultAsync(u => u.LoginIdentifier == new Domain.Identity.LoginIdentifier(login));
        if (existing is not null)
        {
            return existing;
        }

        var user = TestUsers.NewUser(login);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }
}
