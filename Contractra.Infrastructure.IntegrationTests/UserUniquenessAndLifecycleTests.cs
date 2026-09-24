using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.IntegrationTests;

public class UserUniquenessAndLifecycleTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public UserUniquenessAndLifecycleTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("Exact.User01", "Exact.User01")]
    [InlineData("Kamel", "kamel")]
    [InlineData("Mona", "MONA")]
    [InlineData("Omar.Field01", "omar.FIELD01")]
    public async Task LoginIdentifier_IsUnique_CaseInsensitively(string existing, string duplicate)
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Users.Add(new User(new LoginIdentifier(existing), UserStatus.Active));
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.Users.Add(new User(new LoginIdentifier(duplicate), UserStatus.Active));

        await PostgresAssert.FailsWithAsync(() => dbContext2.SaveChangesAsync(), "23505", "ux_users_login_identifier_lower");
    }

    [Fact]
    public async Task DistinctLoginIdentifiers_AreAllowed()
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Users.Add(new User(new LoginIdentifier("Distinct.UserA"), UserStatus.Active));
        dbContext.Users.Add(new User(new LoginIdentifier("Distinct.UserB"), UserStatus.Active));

        await dbContext.SaveChangesAsync(); // must not throw
    }

    [Fact]
    public async Task CaseOnlyDuplicate_IsRejected_EvenThroughRawSql()
    {
        await using var connection = await OpenConnectionAsync();
        const string insert = "INSERT INTO users (id, login_identifier, status) VALUES (@id, @login_identifier, 'Active');";

        await PostgresAssert.ExecuteAsync(connection, null, insert,
            ("id", Guid.CreateVersion7()), ("login_identifier", "RawSql.Kamel"));

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, insert,
                ("id", Guid.CreateVersion7()), ("login_identifier", "rawsql.KAMEL")),
            "23505",
            "ux_users_login_identifier_lower");
    }

    [Fact]
    public async Task StoredLoginIdentifier_KeepsTheSuppliedValueExactly()
    {
        var user = new User(new LoginIdentifier("Admin.User01"), UserStatus.Disabled);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var stored = await verifyContext.Users.SingleAsync(u => u.Id == user.Id);

        Assert.Equal("Admin.User01", stored.LoginIdentifier.Value);
        Assert.Equal(UserStatus.Disabled, stored.Status);
    }

    [Theory]
    [InlineData("Suspended")]
    [InlineData("active")]
    [InlineData("disabled")]
    public async Task StatusCheckConstraint_RejectsUndocumentedValues(string status)
    {
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(
                connection,
                null,
                "INSERT INTO users (id, login_identifier, status) VALUES (@id, @login_identifier, @status);",
                ("id", Guid.CreateVersion7()),
                ("login_identifier", $"StatusCheck.{Guid.NewGuid():N}"),
                ("status", status)),
            "23514",
            "ck_users_status_valid_values");
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Disabled")]
    public async Task StatusCheckConstraint_AcceptsDocumentedValues(string status)
    {
        await using var connection = await OpenConnectionAsync();

        var inserted = await PostgresAssert.ExecuteAsync(
            connection,
            null,
            "INSERT INTO users (id, login_identifier, status) VALUES (@id, @login_identifier, @status);",
            ("id", Guid.CreateVersion7()),
            ("login_identifier", $"StatusCheck.{Guid.NewGuid():N}"),
            ("status", status));

        Assert.Equal(1, inserted);
    }

    private async Task<Npgsql.NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
