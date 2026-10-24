namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Test classes that open many connections at once (up to 50) to race
/// writers against each other. They run one at a time, after the parallel
/// classes: together they would exceed the test server's max_connections
/// (100, 97 for non-superusers) and fail with 53300. Each class still gets its
/// own ephemeral database through PostgresTestDatabaseFixture.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConcurrencyTestCollection
{
    public const string Name = "Concurrency tests (run alone)";
}
