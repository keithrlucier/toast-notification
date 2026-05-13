using System.Runtime.InteropServices;
using Testcontainers.PostgreSql;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Spins up a PostgreSQL 16 container for the test run and exposes its
/// connection string. Fixture is collection-scoped — every test class that
/// joins <see cref="LoadCollection"/> shares the same container lifecycle
/// (the collection also owns a <see cref="LoadFixture"/> that consumes this
/// connection string for the shared <see cref="ApiTestFactory"/>).
/// The Api project's <see cref="Program"/> runs <c>db.Database.Migrate()</c>
/// on startup, so the schema is applied automatically when the
/// <see cref="ApiTestFactory"/> boots against the connection string.
///
/// Fallback: if the environment variable <c>TOAST_TEST_CONNECTION_STRING</c>
/// is set, the fixture skips Docker entirely and uses that connection string.
/// This lets the test run in environments without Docker (CI service
/// containers, dev machines with local Postgres).
///
/// Docker pre-flight: when the env override is absent and Docker is
/// unreachable, <see cref="InitializeAsync"/> throws a targeted
/// <see cref="InvalidOperationException"/> with a friendly message pointing
/// the developer at the env-var override. Without this, Testcontainers
/// surfaces an internal connection failure stack trace that isn't actionable.
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

        if (!IsDockerReachable())
        {
            throw new InvalidOperationException(
                "Docker is not reachable from this process. The integration tests need either " +
                "(a) a running Docker daemon so Testcontainers can spin up postgres:16-alpine, or " +
                $"(b) the environment variable {EnvOverrideKey} set to a Postgres 16 connection string. " +
                "On the CI runner, set the env var alongside a Postgres service container.");
        }

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is null) return;
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Cheap, side-effect-free probe — does the platform-specific Docker
    /// endpoint exist on disk? On Linux/macOS the daemon listens on the
    /// <c>/var/run/docker.sock</c> Unix socket; on Windows the named pipe
    /// <c>\\.\pipe\docker_engine</c> is exposed. Existence of the file/pipe
    /// is a strong-enough signal that Docker is installed and running for
    /// our friendly-error purposes; full reachability is left to Testcontainers
    /// (which surfaces real connection errors only after this gate passes).
    /// </summary>
    private static bool IsDockerReachable()
    {
        // Allow override of the socket location via the standard Docker env var.
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost)) return true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return File.Exists(@"\\.\pipe\docker_engine");

        return File.Exists("/var/run/docker.sock");
    }
}
