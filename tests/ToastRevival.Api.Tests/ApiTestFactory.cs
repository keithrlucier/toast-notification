using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ToastRevival.Api.Services;

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
    private readonly IReadOnlyDictionary<string, string?>? _extraConfig;

    public ApiTestFactory(string connectionString, IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        _connectionString = connectionString;
        _extraConfig = extraConfig;
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
            //
            // WebApplicationFactory<Program> roots the ContentRoot at the API
            // project directory (src/ToastRevival.Api/) at runtime, but our
            // appsettings.Test.json is copied to the TEST assembly's output
            // dir by the csproj. Use the absolute path off the test assembly
            // so the resolver doesn't go looking in src/ToastRevival.Api/.
            var testAssemblyDir = System.IO.Path.GetDirectoryName(typeof(ApiTestFactory).Assembly.Location)!;
            config.AddJsonFile(System.IO.Path.Combine(testAssemblyDir, "appsettings.Test.json"), optional: false);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
            });

            // Per-test config overrides (e.g. TOAST_REQUIRE_BILLING=true for the
            // trial cap concurrency regression). Layered last so they win.
            if (_extraConfig is not null)
            {
                config.AddInMemoryCollection(_extraConfig);
            }
        });

        builder.ConfigureServices((ctx, services) =>
        {
            // Program.cs captures Jwt:Key/Issuer/Audience from builder.Configuration at
            // service-registration time — before our ConfigureAppConfiguration callback
            // appends appsettings.Test.json. PostConfigure runs after all Configure calls
            // and sees the fully-resolved test config, so the JWT middleware validates
            // against the same key/issuer/audience that TokenService uses to sign tokens.
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                opts =>
                {
                    var key = ctx.Configuration["Jwt:Key"]!;
                    opts.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
                    opts.TokenValidationParameters.ValidIssuer =
                        ctx.Configuration["Jwt:Issuer"];
                    opts.TokenValidationParameters.ValidAudience =
                        ctx.Configuration["Jwt:Audience"];
                });

            // Replace real email/SMS senders with no-ops so tests that exercise auth
            // flows don't need real credentials and don't fire external API calls.
            services.AddTransient<IEmailService, NullEmailService>();
            services.AddTransient<ISmsService, NullSmsService>();
        });
    }
}

file sealed class NullEmailService : IEmailService
{
    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
        => Task.CompletedTask;
}

file sealed class NullSmsService : ISmsService
{
    public Task SendAsync(string toPhone, string message)
        => Task.CompletedTask;
}
