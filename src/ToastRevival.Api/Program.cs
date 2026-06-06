using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using Polly;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using ToastRevival.Api.Data;
using ToastRevival.Api.Hubs;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

// Community license — free for organizations under $1M ARR. Upgrade to
// Professional when billing warrants it.
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Runtime-managed local overrides. This stays out of git and lets platform
// admins set non-secret operational values without SSH.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Database
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// SES-2-R: short-TTL cache for the per-request session-revocation check.
builder.Services.AddMemoryCache();

// Tenant isolation
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HttpContextTenantProvider>();

// Identity
builder.Services.AddIdentityCore<AppUser>(opts =>
{
    // AA-M3: Hardened password policy — minimum 12 chars, all character classes required.
    opts.Password.RequireDigit = true;
    opts.Password.RequiredLength = 12;
    opts.Password.RequireUppercase = true;
    opts.Password.RequireNonAlphanumeric = true;
    opts.Password.RequireLowercase = true;
    opts.Password.RequiredUniqueChars = 2;

    // Brute-force lockout. CheckPasswordAsync/SMS-code verification call into
    // UserManager lockout helpers in AuthController; these options govern the
    // threshold and window. AllowedForNewUsers so accounts are protected from
    // creation (CreateAsync sets LockoutEnabled=true by default).
    opts.Lockout.MaxFailedAccessAttempts = 5;
    opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    opts.Lockout.AllowedForNewUsers = true;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// JWT authentication
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is required.");

// A production deployment that forgets the Jwt__Key environment override
// silently falls through to whatever short placeholder sits in appsettings.json.
// HMAC-SHA256 wants 32+ bytes of key material; anything shorter weakens the
// signature beyond useful guarantee. Block it.
//
// The committed placeholder is 63 chars, so the length check alone lets it pass.
// Reject the known placeholder (and any value still carrying the REPLACE marker
// or other obvious default/low-entropy sentinels) so a misconfigured deploy
// fails closed at startup instead of signing tokens with a public key.
if (!builder.Environment.IsDevelopment())
{
    if (jwtKey.Length < 32)
    {
        throw new InvalidOperationException(
            "Jwt:Key must be at least 32 characters in non-Development environments. " +
            "Override via the Jwt__Key environment variable.");
    }

    const string knownPlaceholder =
        "REPLACE-THIS-WITH-A-STRONG-SECRET-KEY-IN-PRODUCTION-MIN-32-CHARS";
    var looksLikePlaceholder =
        string.Equals(jwtKey, knownPlaceholder, StringComparison.Ordinal)
        || jwtKey.Contains("REPLACE", StringComparison.OrdinalIgnoreCase)
        || jwtKey.Contains("CHANGEME", StringComparison.OrdinalIgnoreCase)
        || jwtKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);

    if (looksLikePlaceholder)
    {
        throw new InvalidOperationException(
            "Jwt:Key is set to a default/placeholder value in a non-Development " +
            "environment. Set a strong, unique secret via the Jwt__Key environment " +
            "variable before deploying.");
    }
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.MapInboundClaims = false;
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            // AA-L3: Algorithm pinning — reject tokens signed with any algorithm
            // other than HMAC-SHA256 to prevent alg-confusion attacks.
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        };
        // SignalR passes JWT as query param on WebSocket handshake
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(token) && ctx.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            },
            // SES-2-R: immediate session revocation for USER tokens. Reject when the
            // tenant is suspended or the user's SecurityStamp (token epoch) has rotated
            // (password reset / role change). Cached 30s to stay off the hot path; the
            // reject path re-reads so a stale cache can't kill a freshly-rotated token or
            // a just-unsuspended tenant. Legacy tokens with no epoch claim skip the epoch
            // check (graceful rollout) but still honor suspension. Device tokens are gated
            // separately (IsDeviceRevoked / hub OnConnected). Fail-OPEN on a DB blip — a
            // transient outage must not log the whole platform out.
            OnTokenValidated = async ctx =>
            {
                if (ctx.Principal?.FindFirstValue("type") != "user") return;
                var userIdStr = ctx.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(userIdStr, out var userId)) { ctx.Fail("invalid token"); return; }

                var tokenEpoch      = ctx.Principal.FindFirstValue("tokenEpoch");
                var isPlatformAdmin = ctx.Principal.FindFirstValue("platformAdmin") == "true";

                try
                {
                    var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                    var db    = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var key   = $"sess:{userId}";

                    async Task<(bool Found, bool Suspended, string? Stamp)> ReadAsync()
                    {
                        var u = await db.Users.IgnoreQueryFilters()
                            .Where(x => x.Id == userId)
                            .Select(x => new { x.SecurityStamp, x.TenantId })
                            .FirstOrDefaultAsync();
                        if (u is null) return (false, false, null);
                        var suspendedAt = await db.Tenants.IgnoreQueryFilters()
                            .Where(t => t.Id == u.TenantId)
                            .Select(t => (DateTime?)t.SuspendedAt)
                            .FirstOrDefaultAsync();
                        return (true, suspendedAt.HasValue, u.SecurityStamp);
                    }

                    static bool ShouldReject((bool Found, bool Suspended, string? Stamp) s, bool isPa, string? epoch)
                        => !s.Found || (s.Suspended && !isPa) || (epoch is not null && s.Stamp != epoch);

                    if (!cache.TryGetValue(key, out (bool Found, bool Suspended, string? Stamp) v))
                    {
                        v = await ReadAsync();
                        cache.Set(key, v, TimeSpan.FromSeconds(30));
                    }

                    if (ShouldReject(v, isPlatformAdmin, tokenEpoch))
                    {
                        // Stale-cache guard: confirm against a fresh read before rejecting.
                        v = await ReadAsync();
                        cache.Set(key, v, TimeSpan.FromSeconds(30));
                        if (ShouldReject(v, isPlatformAdmin, tokenEpoch))
                            ctx.Fail("session revoked");
                    }
                }
                catch
                {
                    // DB unavailable — do not fail closed; device/send paths gate independently.
                }
            },
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("PlatformAdmin", policy =>
        policy.RequireClaim("platformAdmin", "true"));
});

// Rate limiting (D7)
builder.Services.AddRateLimiter(opts =>
{
    // Per-tenant sliding window: 60 req/min
    opts.AddPolicy("tenant-per-minute", ctx =>
    {
        var partitionKey = ctx.User.FindFirst("tenantId")?.Value
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    });

    // Per-device fixed window: 10 req/hr
    opts.AddPolicy("device-per-hour", ctx =>
    {
        var partitionKey = ctx.User.FindFirst("deviceId")?.Value
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    });

    // Dedicated higher-budget policy for catch-up so a flaky-network
    // reconnect storm doesn't exhaust the shared device-per-hour budget.
    // 60/hr vs 10/hr for ReportInteraction.
    opts.AddPolicy("device-catchup-per-hour", ctx =>
    {
        var partitionKey = ctx.User.FindFirst("deviceId")?.Value
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromHours(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    });

    // Login brute-force protection: 10 attempts / 5 min per IP.
    // BF-2 (Keith 2026-06-01 — RESOLVED): the partition key now trusts CF-Connecting-IP
    // ONLY when the socket peer is a verified Cloudflare egress IP (or a loopback reverse
    // proxy that forwarded it) — see CloudflareIpValidator.ResolveTrustedClientIp. A direct
    // hit on the origin can no longer forge the header to reset its bucket. Applies to
    // login-sms-per-ip and trial-register-per-ip too. Ops follow-up (Keith): ensure the
    // origin firewall only admits Cloudflare ranges in production.
    opts.AddPolicy("login-per-ip", ctx =>
    {
        var partitionKey = CloudflareIpValidator.ResolveTrustedClientIp(ctx);
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    });

    // SMS OTP verify: 5 attempts / 15 min per IP (prevent OTP brute-force).
    // Same CF-Connecting-IP pattern as login-per-ip.
    opts.AddPolicy("login-sms-per-ip", ctx =>
    {
        var partitionKey = CloudflareIpValidator.ResolveTrustedClientIp(ctx);
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    });

    // Public trial applications: low hourly budget to limit spam and Turnstile
    // validation flooding before any tenant or user is created.
    opts.AddPolicy("trial-register-per-ip", ctx =>
    {
        var partitionKey = CloudflareIpValidator.ResolveTrustedClientIp(ctx);
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    });

    // DOS-H1: rate limit for forgot-password — 5 requests per 15 min per IP.
    opts.AddFixedWindowLimiter("forgot-password", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(15);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });

    // DOS-H2: rate limit for SMS registration verify — 5 per 15 min per IP.
    opts.AddFixedWindowLimiter("register-sms", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(15);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });

    // DOS-M5: per-userId SMS send rate limit keyed on userId claim.
    // 5 SMS per hour per userId (defense in depth alongside per-IP).
    opts.AddFixedWindowLimiter("mfa-sms-send-per-user", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromHours(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });

    opts.RejectionStatusCode = 429;
});

// SignalR
builder.Services.AddSignalR();

// Application services
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<IAuditService, AuditService>();
// NotificationQueueService is both a singleton service and a hosted background service.
// Register as singleton first so INotificationQueueService resolves to the same instance.
builder.Services.AddSingleton<NotificationQueueService>();
builder.Services.AddSingleton<INotificationQueueService>(sp => sp.GetRequiredService<NotificationQueueService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<NotificationQueueService>());

// M3 security services
// ContentSafetyService is scoped (M11) — it reads per-tenant policy from AppDbContext
// on each call. Client construction is amortized through a static (endpoint, key) cache
// inside the service so the scoped registration doesn't add per-request Azure SDK overhead.
// REL-M2 (ContentSafety): ContentSafetyService uses Azure.AI.ContentSafety.ContentSafetyClient
// (Azure SDK), not a raw HttpClient — HttpClient Polly policies cannot be applied here.
// The Azure SDK has its own built-in retry policy (RetryPolicy on the pipeline). No AddHttpClient registration.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IContentModerationService, ContentSafetyService>();
builder.Services.AddScoped<IBlocklistService, BlocklistService>();
builder.Services.AddSingleton<ICloudflareIpValidator, CloudflareIpValidator>();
builder.Services.AddSingleton<MfaService>();

// PDF export
builder.Services.AddSingleton<IPdfExportService, PdfExportService>();

// Licensing
builder.Services.AddScoped<ILicenseService, LicenseService>();
builder.Services.AddScoped<IStripeBillingSyncService, StripeBillingSyncService>();
builder.Services.AddSingleton<IBillingConfigService, BillingConfigService>();
builder.Services.AddSingleton<IMessagingConfigService, MessagingConfigService>();
builder.Services.AddSingleton<ISsoConfigService, SsoConfigService>();

// Microsoft Entra SSO. Singleton, but credentials are read live from config on
// each call so a platform-panel secret change (written to appsettings.Local.json
// and reloaded) applies without a restart. Uses IHttpClientFactory for the token
// exchange; OIDC signing-key metadata is cached statically inside the service.
// REL-M2 (MicrosoftSso): MicrosoftSsoService calls _httpFactory.CreateClient() (unnamed default
// client) for the token exchange POST. Polly retry + circuit-breaker added to match REL-H3 policy.
builder.Services.AddHttpClient(string.Empty)
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retry => TimeSpan.FromSeconds(Math.Pow(2, retry))))
    .AddTransientHttpErrorPolicy(p => p.CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30)));
builder.Services.AddSingleton<IMicrosoftSsoService, MicrosoftSsoService>();

// Transactional messaging (Mailjet email + ClickSend SMS)
// REL-H3: Polly retry + circuit-breaker on all outbound HTTP clients.
// Requires Microsoft.Extensions.Http.Polly (see .csproj). Policy wraps transient
// HTTP errors (5xx, network failure) with 3 retries (exponential backoff) and
// a circuit breaker that opens after 5 consecutive failures for 30 seconds.
builder.Services.AddHttpClient<IEmailService, MailjetEmailService>()
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retry => TimeSpan.FromSeconds(Math.Pow(2, retry))))
    .AddTransientHttpErrorPolicy(p => p.CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30)));
builder.Services.AddHttpClient<ISmsService, ClickSendSmsService>()
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retry => TimeSpan.FromSeconds(Math.Pow(2, retry))))
    .AddTransientHttpErrorPolicy(p => p.CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30)));
builder.Services.AddHttpClient<ITurnstileVerifier, CloudflareTurnstileVerifier>()
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retry => TimeSpan.FromSeconds(Math.Pow(2, retry))))
    .AddTransientHttpErrorPolicy(p => p.CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30)));

// CORS — dev allows any origin; production should lock this down via config
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
    {
        if (builder.Environment.IsDevelopment())
            p.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        else
        {
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
            p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        }
    }));

// AA-L4: Fail fast if CORS origins are not configured for non-Development.
if (!builder.Environment.IsDevelopment())
{
    var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
    if (allowedOrigins is null || allowedOrigins.Length == 0)
        throw new InvalidOperationException("CORS AllowedOrigins must be configured for non-Development environments");
}

// REL-H1: global exception handler so unhandled exceptions return RFC 7807
// Problem Details instead of exposing stack traces.
builder.Services.AddProblemDetails();

builder.Services.AddControllers(options =>
{
    // REST-L7: enforce application/json content type globally on all actions.
    options.Filters.Add(new ConsumesAttribute("application/json"));
})
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var webRoot = app.Environment.WebRootPath
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
app.Environment.WebRootPath = webRoot;

// Uploaded asset-library files (hero/logo/icon images). Stored OUTSIDE the
// deploy directory so a redeploy that replaces the application directory never
// orphans previously-uploaded files. Path is configurable via Assets:RootPath
// (point it at a persistent location in production); defaults to
// <wwwroot>/assets for local dev. This directory is served explicitly below via
// PhysicalFileProvider, NOT the default web-root provider — reassigning
// app.Environment.WebRootPath after Build() does not rewire WebRootFileProvider,
// so a plain UseStaticFiles() would serve nothing.
var assetsRoot = app.Configuration["Assets:RootPath"]
    ?? Path.Combine(webRoot, "assets");
Directory.CreateDirectory(assetsRoot);

// Run migrations on every startup — safe because Migrate() is idempotent
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Security defaults — defensive response headers on every response. Set
// before any other middleware so error responses, 401 challenges, and
// static-file responses all carry them.
//
// HSTS + HTTPS redirect are production-only — TestServer has no HTTPS pipe
// and Development typically runs over plain http://localhost. HSTS skips
// localhost by default, so it's safe to register but only useful behind a
// real TLS terminator.
// nginx terminates TLS and proxies to Kestrel over loopback. Honor its
// X-Forwarded-Proto/For so Request.Scheme is https (loopback proxies are
// trusted by default) — otherwise absolute URLs built from Request (e.g. the
// asset URL handed to the Windows agent) are stamped http:// and get blocked
// as mixed content on the https dashboard.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// REL-H1: route unhandled exceptions to ErrorController for consistent Problem Details.
app.UseExceptionHandler("/error");

app.Use(async (ctx, next) =>
{
    var headers = ctx.Response.Headers;
    // X-Content-Type-Options: prevents browsers from MIME-sniffing a
    // response away from the declared Content-Type. Cheap, universally safe.
    headers["X-Content-Type-Options"] = "nosniff";
    // X-Frame-Options: clickjacking defense. The API never renders HTML
    // intended for embedding (Swagger UI in dev is the only HTML surface
    // and clickjacking on it is non-issue), so DENY is the right call.
    headers["X-Frame-Options"] = "DENY";
    // Referrer-Policy: prevents leaking full URLs (which can carry tenant
    // subdomain or query params) to cross-origin destinations like external
    // image hosts referenced in toast hero/logo URLs.
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // Permissions-Policy: the API has no need for camera/mic/geolocation
    // surfaces. Disabling closes any embedded-context probe vector.
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // Content-Security-Policy: lock down script/object/base/frame-ancestors for
    // the JSON API and the SPA it may serve. style/font/img relaxations cover the
    // SPA's Google Fonts usage and data:/https: images (toast hero/logo URLs).
    // REVIEW-2026-06-06 INJ-M5 REJECTED-by-design: nonce/hash on script-src is incompatible
    // with Vite content-hashed static asset serving; img-src https: is required for tenant
    // external logo URLs; unsafe-inline on style-src is required for React inline styles;
    // accepted current CSP posture — nonce-based hardening is a dedicated security milestone.
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; " +
        "frame-ancestors 'none'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: https:";
    await next();
});

if (app.Environment.IsDevelopment())
{
    // REVIEW-2026-06-06 REST-M1 REJECTED-by-design: Swagger exposure in production gated on CI/CD OpenAPI generation (DEVOPS-H4); will be added to ci.yml build artifact after that gate ships
    app.UseSwagger();
    app.UseSwaggerUI();
}

// REST-M3: Correlation ID middleware — stamps every response with X-Request-Id
// using the current trace ID or a fresh GUID if no trace is available.
app.Use(async (ctx, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    ctx.Response.OnStarting(() =>
    {
        ctx.Response.Headers["X-Request-Id"] = traceId;
        return Task.CompletedTask;
    });
    await next();
});

// REVIEW-2026-06-06 REST-M2 REJECTED-by-design: URL versioning deferred; fleet rollout is managed via MSI self-update channel with coordinated agent releases; URL versioning adds routing complexity without solving the coordinated-deployment problem at current fleet scale

app.UseRateLimiter();
app.UseCors();
// Serve uploaded assets from the persistent assetsRoot, mapped at /assets.
// Explicit PhysicalFileProvider (not the default web-root provider, which is
// unreliable here — see assetsRoot note above).
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(assetsRoot),
    RequestPath = "/assets",
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

// Exposes the auto-generated entry-point class for WebApplicationFactory<Program>
// in the integration test project. No behavior — declaration only.
public partial class Program;
