using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// The EF Core model and the migration snapshot agree, so no migration is
/// missing. Replaces running `dotnet ef migrations has-pending-model-changes`
/// by hand, and works where the dotnet-ef tool is not installed.
/// </summary>
public class ModelConsistencyTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public ModelConsistencyTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void TheModel_HasNoChangesMissingFromTheMigrations()
    {
        using var dbContext = _fixture.CreateDbContext();

        Assert.False(dbContext.Database.HasPendingModelChanges());
    }
}
