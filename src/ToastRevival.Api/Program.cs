using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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

// Tenant isolation
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HttpContextTenantProvider>();

// Identity
builder.Services.AddIdentityCore<AppUser>(opts =>
{
    opts.Password.RequireDigit = true;
    opts.Password.RequiredLength = 8;
    opts.Password.RequireUppercase = false;
    opts.Password.RequireNonAlphanumeric = false;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// JWT authentication
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is required.");

// Closes INFO-M1-006: a production deployment that forgets the Jwt__Key
// environment override silently falls through to whatever short placeholder
// sits in appsettings.json. HMAC-SHA256 wants 32+ bytes of key material;
// anything shorter weakens the signature beyond useful guarantee. Block it.
if (!builder.Environment.IsDevelopment() && jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 characters in non-Development environments. " +
        "Override via the Jwt__Key environment variable.");
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

    // INFO-M2B-005: dedicated higher-budget policy for catch-up so a
    // flaky-network reconnect storm doesn't exhaust the shared device-per-hour
    // budget. 60/hr vs 10/hr for ReportInteraction.
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
builder.Services.AddSingleton<IContentModerationService, ContentSafetyService>();
builder.Services.AddScoped<IBlocklistService, BlocklistService>();
builder.Services.AddSingleton<MfaService>();

// M5.D export
builder.Services.AddSingleton<IPdfExportService, PdfExportService>();

// M6 licensing
builder.Services.AddScoped<ILicenseService, LicenseService>();
builder.Services.AddScoped<IStripeBillingSyncService, StripeBillingSyncService>();
builder.Services.AddSingleton<IBillingConfigService, BillingConfigService>();

// M9.A transactional messaging (Mailjet email + ClickSend SMS)
builder.Services.AddHttpClient<IEmailService, MailjetEmailService>();
builder.Services.AddHttpClient<ISmsService, ClickSendSmsService>();

// CORS — dev allows any origin; production should lock this down via config
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
    {
        if (builder.Environment.IsDevelopment())
            p.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        else
            p.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [])
             .AllowAnyMethod().AllowAnyHeader().AllowCredentials();
    }));

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Ensure wwwroot/assets directory exists for uploaded files
var webRoot = app.Environment.WebRootPath
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(Path.Combine(webRoot, "assets"));
app.Environment.WebRootPath = webRoot;

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
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

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
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();
app.UseCors();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

// Exposes the auto-generated entry-point class for WebApplicationFactory<Program>
// in the integration test project. No behavior — declaration only.
public partial class Program;
