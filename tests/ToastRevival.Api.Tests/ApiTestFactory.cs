using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Boots the production API on top of TestServer with the connection string
/// rewritten to point at the fixture's PostgreSQL container. Identity, JWT,
/// SignalR, the hosted NotificationQueueService, and the on-startup
/// <c>db.Database.Migrate()</c> all run normally — this is a real end-to-end
/// stack against an isolated database.
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ApiTestFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Force Production environment so Swagger doesn't load and the
        // CORS policy uses the configured AllowedOrigins (empty) instead of
        // SetIsOriginAllowed(_ => true). Cross-origin is not part of the test
        // surface — TestServer is same-origin.
        builder.UseEnvironment("Production");
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            // Layer test settings on top so JWT key, dummy Stripe values, and
            // logging tweaks are applied. Then override the connection string
            // last so the test fixture's container wins regardless of source.
            config.AddJsonFile("appsettings.Test.json", optional: false);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
            });
        });
    }
}
