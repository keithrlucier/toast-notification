using Testcontainers.PostgreSql;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Spins up a PostgreSQL 16 container for the test run and exposes its
/// connection string. Fixture is class-scoped — each test class that consumes
/// it gets its own database lifecycle. The Api project's <see cref="Program"/>
/// runs <c>db.Database.Migrate()</c> on startup, so the schema is applied
/// automatically when the WebApplicationFactory boots.
///
/// Fallback: if the environment variable <c>TOAST_TEST_CONNECTION_STRING</c>
/// is set, the fixture skips Docker and uses that connection string instead.
/// This lets the test run in environments without Docker (CI service
/// containers, dev machines with local Postgres).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string EnvOverrideKey = "TOAST_TEST_CONNECTION_STRING";

    private readonly PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public PostgresFixture()
    {
        var envOverride = Environment.GetEnvironmentVariable(EnvOverrideKey);
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            ConnectionString = envOverride;
            _container = null;
            return;
        }

        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("toastrevival_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }

    public async Task InitializeAsync()
    {
        if (_container is null) return;
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is null) return;
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
