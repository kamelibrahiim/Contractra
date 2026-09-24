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

    [Fact]
    public async Task LoginIdentifier_IsUnique()
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Users.Add(new User(new LoginIdentifier("Unique.User01"), UserStatus.Active));
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.Users.Add(new User(new LoginIdentifier("Unique.User01"), UserStatus.Active));

        await PostgresAssert.FailsWithAsync(() => dbContext2.SaveChangesAsync(), "23505", "ux_users_login_identifier");
    }

    [Fact]
    public async Task LoginIdentifier_UniquenessIsCaseSensitive()
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Users.Add(new User(new LoginIdentifier("CaseSensitive.User"), UserStatus.Active));
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.Users.Add(new User(new LoginIdentifier("casesensitive.user"), UserStatus.Active));

        await dbContext2.SaveChangesAsync(); // must not throw: uniqueness is case-sensitive, no lower() rule is documented for login_identifier
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
