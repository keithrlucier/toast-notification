using Npgsql;
using Respawn;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Collection-scoped fixture that boots one <see cref="ApiTestFactory"/> on top
/// of <see cref="PostgresFixture"/> and exposes a <see cref="Respawner"/> for
/// per-test database cleanup. Closes INFO-M8A-002: the M8.A pattern of building
/// a fresh factory per test is fine for one test but quadratic-ish for a load
/// suite — the load harness opens hundreds of SignalR connections per run, so
/// the cost of a per-test web host is real.
///
/// Lifecycle:
///   - <see cref="InitializeAsync"/> warms the API once (forces
///     <c>db.Database.Migrate()</c> via <see cref="ApiTestFactory.CreateClient"/>),
///     then captures a Respawner snapshot of the empty schema. Subsequent
///     <see cref="ResetAsync"/> calls truncate every non-Identity table back to
///     the snapshot in milliseconds — much faster than recreating the schema.
///   - Tests that need an isolated tenant per run call <see cref="ResetAsync"/>
///     in their constructor (or per-fact) before seeding. Without the call, the
///     fixture's data accumulates across the run.
///
/// Sharing rule: collection-scoped via <see cref="LoadCollection"/>; both
/// <see cref="EndToEndNotificationTests"/> and the load suite share the same
/// factory instance to amortize startup, but each test resets the database to
/// preserve isolation.
/// </summary>
public sealed class LoadFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;

    public ApiTestFactory Factory { get; private set; } = null!;

    /// <summary>
    /// The Postgres container fixture that backs this run. Test classes that
    /// previously took <c>PostgresFixture</c> in their constructor now take
    /// <see cref="LoadFixture"/> and read this property — xUnit 2 cannot wire
    /// one collection fixture into another's constructor, so the collection
    /// owns only <c>LoadFixture</c> and <c>LoadFixture</c> owns Postgres.
    /// </summary>
    public PostgresFixture Postgres => _postgres;

    /// <summary>
    /// Respawner is null when the connection string targets a database the
    /// snapshot couldn't initialize against (e.g. Docker-less env override
    /// pointed at a managed instance with restricted DDL). Tests that need a
    /// clean slate must check the property and fall back to per-test factory
    /// reconstruction; the load suite skips itself when Respawner is null
    /// because re-seeding 1,000 devices on every assertion isn't viable.
    /// </summary>
    public Respawner? Respawner { get; private set; }

    public string ConnectionString => _postgres.ConnectionString;

    public LoadFixture()
    {
        // xUnit 2.9 collection fixtures cannot take other collection fixtures
        // as constructor arguments — the runtime fails with
        // "had one or more unresolved constructor arguments". So LoadFixture
        // owns the PostgresFixture lifecycle directly and the collection
        // definition exposes only LoadFixture.
        _postgres = new PostgresFixture();
    }

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();

        Factory = new ApiTestFactory(_postgres.ConnectionString);

        // Force the host to boot (which runs the migration hook) by issuing one
        // request. The endpoint shape is unimportant — we just need the
        // pipeline + DI graph live so the schema exists before the Respawner
        // snapshot is taken.
        using (var http = Factory.CreateClient())
        {
            // /api/templates is authenticated; the 401 still proves the host is up.
            await http.GetAsync("/api/templates");
        }

        try
        {
            await using var conn = new NpgsqlConnection(_postgres.ConnectionString);
            await conn.OpenAsync();
            Respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = new[] { "public" },
                TablesToIgnore = new Respawn.Graph.Table[]
                {
                    new("__EFMigrationsHistory"),
                },
            });
        }
        catch
        {
            // The fixture stays usable without per-test cleanup — the load
            // suite skips itself, the E2E test still works because each test
            // generates fresh GUIDs for tenant + device + email.
            Respawner = null;
        }
    }

    public async Task ResetAsync()
    {
        if (Respawner is null) return;
        await using var conn = new NpgsqlConnection(_postgres.ConnectionString);
        await conn.OpenAsync();
        await Respawner.ResetAsync(conn);
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(nameof(LoadCollection))]
public sealed class LoadCollection
    : ICollectionFixture<LoadFixture>
{
}
