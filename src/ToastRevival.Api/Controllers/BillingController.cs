using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using ToastRevival.Api.Data;
using ToastRevival.Api.Extensions;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;
using ToastRevival.Api.Utilities;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class BillingController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILicenseService _license;
    private readonly IBillingConfigService _billingConfig;
    private readonly IConfiguration _config;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        AppDbContext db,
        ILicenseService license,
        IBillingConfigService billingConfig,
        IConfiguration config,
        ILogger<BillingController> logger)
    {
        _db = db;
        _license = license;
        _billingConfig = billingConfig;
        _config = config;
        _logger = logger;
    }

    // ── GET /api/billing/plan ─────────────────────────────────────────────────

    [HttpGet("plan")]
    public async Task<IActionResult> Plan()
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return NotFound();

        await _license.SyncConsumedCountAsync(tenant);

        var deviceCount = tenant.ConsumedCount;
        var billableDevices = BillingPlanRules.BillableDevices(deviceCount);

        return Ok(new
        {
            planName          = "Standard",
            pricePerDevice    = BillingPlanRules.PricePerDevice,
            freeTierLimit     = BillingPlanRules.FreeTierDeviceLimit,
            deviceCount,
            billableDevices,
            currentBill       = billableDevices * BillingPlanRules.PricePerDevice,
            billingStatus    = tenant.BillingStatus.ToString(),
            licenseStart     = tenant.LicenseStart,
            licenseEnd       = tenant.LicenseEnd,
            trialEnd         = tenant.BillingStatus == BillingStatus.Trialing ? tenant.LicenseEnd : null,
            stripeCustomerId = tenant.StripeCustomerId,
            billingEnabled   = _config.GetValue<bool>("Billing:Enabled"),
        });
    }

    // ── GET  /api/billing/admin/stripe-config ────────────────────────────────
    // ── POST /api/billing/admin/stripe-config ────────────────────────────────

    [HttpGet("admin/stripe-config")]
    public IActionResult GetStripeConfig()
    {
        if (!IsPlatformAdmin()) return Forbid();
        return Ok(_billingConfig.GetSnapshot());
    }

    [HttpPost("admin/stripe-config")]
    public async Task<IActionResult> UpdateStripeConfig([FromBody] UpdateStripeConfigRequest req)
    {
        if (!IsPlatformAdmin()) return Forbid();
        // AA-M8: POST stripe-config requires fresh MFA elevation (same pattern as SystemController).
        if (RequireFreshMfaCheck() is { } mfaErr) return mfaErr;
        try
        {
            var snapshot = await _billingConfig.UpdateStripeConfigAsync(
                req.SecretKey, req.WebhookSecret, req.PerDevicePriceId);
            return Ok(snapshot);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── POST /api/billing/checkout ────────────────────────────────────────────

    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout()
    {
        if (!IsAdmin()) return Forbid();
        if (!_config.GetValue<bool>("Billing:Enabled"))
            return StatusCode(503, new { message = "Billing is currently disabled on this platform." });

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(tenant.StripeSubscriptionId))
            return BadRequest("Subscription already exists. Use the billing portal to manage it.");

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.StartsWith("sk_test_REPLACE"))
            return StatusCode(503, new { message = "Stripe billing is not configured on this server. Add the Stripe secret key before checkout can start." });

        var billingConfig = _billingConfig.GetSnapshot();
        if (!billingConfig.IsConfigured)
            return StatusCode(503, new { message = "Stripe per-device price ID is not configured. A platform admin can set it in Settings before checkout can start." });

        var priceId = billingConfig.PerDevicePriceId;

        StripeConfiguration.ApiKey = secretKey;
        await _license.SyncConsumedCountAsync(tenant);

        // Ensure Stripe customer exists
        string customerId = tenant.StripeCustomerId ?? await CreateStripeCustomerAsync(tenant);
        if (string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
        {
            tenant.StripeCustomerId = customerId;
            await _db.SaveChangesAsync();
        }

        var billableDevices = BillingPlanRules.BillableDevices(tenant.ConsumedCount);
        var sessionService = new SessionService();
        var sessionOpts = new SessionCreateOptions
        {
            Customer = customerId,
            Mode     = "subscription",
            LineItems =
            [
                new SessionLineItemOptions { Price = priceId, Quantity = Math.Max(1, billableDevices) }
            ],
            SuccessUrl = _config["Stripe:SuccessUrl"] ?? "http://localhost:5173/billing?session=success",
            CancelUrl  = _config["Stripe:CancelUrl"]  ?? "http://localhost:5173/billing",
            ClientReferenceId = tenantId.ToString(),
            Metadata   = new Dictionary<string, string> { ["tenantId"] = tenantId.ToString() },
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                TrialPeriodDays = 14,
                Metadata = new Dictionary<string, string> { ["tenantId"] = tenantId.ToString() },
            },
        };

        // REL-M1: Wrap Stripe SDK call in try/catch; return 503 on StripeException.
        Session session;
        try
        {
            session = await sessionService.CreateAsync(sessionOpts);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe CreateCheckout failed: {Message}", ex.Message);
            return Problem(detail: "Billing checkout is temporarily unavailable. Please try again.", statusCode: 503);
        }
        return Ok(new { url = session.Url });
    }

    // ── POST /api/billing/portal ──────────────────────────────────────────────

    [HttpPost("portal")]
    public async Task<IActionResult> CreatePortal()
    {
        if (!IsAdmin()) return Forbid();
        if (!_config.GetValue<bool>("Billing:Enabled"))
            return StatusCode(503, new { message = "Billing is currently disabled on this platform." });

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return NotFound();

        if (string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
            return BadRequest("No Stripe subscription found. Start a subscription first.");

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.StartsWith("sk_test_REPLACE"))
            return StatusCode(503, "Billing is not configured on this server.");

        StripeConfiguration.ApiKey = secretKey;

        var portalService = new Stripe.BillingPortal.SessionService();
        var portalOpts = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer  = tenant.StripeCustomerId,
            ReturnUrl = _config["Stripe:CancelUrl"] ?? "http://localhost:5173/billing",
        };

        // REL-M1: Wrap Stripe SDK call in try/catch; return 503 on StripeException.
        Stripe.BillingPortal.Session portalSession;
        try
        {
            portalSession = await portalService.CreateAsync(portalOpts);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe CreatePortal failed: {Message}", ex.Message);
            return Problem(detail: "Billing portal is temporarily unavailable. Please try again.", statusCode: 503);
        }
        return Ok(new { url = portalSession.Url });
    }

    // ── GET /api/billing/invoices ─────────────────────────────────────────────

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices()
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return NotFound();

        if (string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
            return Problem(detail: "Stripe billing is not configured.", statusCode: 503);

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.StartsWith("sk_test_REPLACE"))
            // REST-L9: consistent 503 instead of empty-array when Stripe not configured.
            return Problem(detail: "Stripe billing is not configured.", statusCode: 503);

        StripeConfiguration.ApiKey = secretKey;

        var invoiceService = new InvoiceService();
        var invoices = await invoiceService.ListAsync(new InvoiceListOptions
        {
            Customer = tenant.StripeCustomerId,
            Limit    = 12,
        });

        var items = invoices.Data.Select(inv => new
        {
            id          = inv.Id,
            status      = inv.Status,
            amount      = inv.AmountPaid / 100.0m,
            currency    = inv.Currency?.ToUpper(),
            created     = inv.Created,
            periodStart = inv.PeriodStart,
            periodEnd   = inv.PeriodEnd,
            pdfUrl      = inv.InvoicePdf,
            hostedUrl   = inv.HostedInvoiceUrl,
        }).ToList();

        return Ok(new { invoices = items });
    }

    // ── POST /api/billing/webhook ─────────────────────────────────────────────

    [HttpPost("webhook")]
    [AllowAnonymous]
    [DisableRateLimiting]
    // DOS-L2: Cap Stripe webhook body size to 64 KB to prevent large-body DoS.
    [RequestSizeLimit(65_536)]
    public async Task<IActionResult> Webhook()
    {
        if (!_config.GetValue<bool>("Billing:Enabled"))
        {
            _logger.LogInformation("Billing disabled — ignoring Stripe webhook.");
            return Ok();
        }

        var webhookSecret = _config["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret) || webhookSecret.StartsWith("whsec_REPLACE"))
        {
            _logger.LogWarning("Stripe webhook secret not configured — ignoring event.");
            return Ok();
        }

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.StartsWith("sk_test_REPLACE"))
        {
            _logger.LogWarning("Stripe secret key not configured — ignoring webhook.");
            return Ok();
        }

        StripeConfiguration.ApiKey = secretKey;

        // Read raw body before any model binding — required for Stripe-Signature verification
        using var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync();

        var signature = HttpContext.Request.Headers["Stripe-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(signature))
            return BadRequest("Missing Stripe-Signature header.");

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe webhook signature verification failed: {Message}", ex.Message);
            return BadRequest("Invalid signature.");
        }

        // REL-003-R: Persist the event record BEFORE returning 2xx. If we crash after
        // ack but before processing, the row exists for a recovery sweep. If Stripe
        // replays the same event, the unique index on EventId returns a duplicate-key
        // exception which we convert to 200 (idempotent accept, already in inbox).
        var inboxEvent = new StripeWebhookEvent
        {
            EventId   = stripeEvent.Id,
            EventType = stripeEvent.Type,
            Status    = "received",
            ReceivedAt = DateTime.UtcNow,
        };
        try
        {
            _db.StripeWebhookEvents.Add(inboxEvent);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is Npgsql.NpgsqlException pg && pg.SqlState == "23505")
        {
            // Stripe replay — already persisted; return 200 to stop retries.
            _logger.LogInformation("Stripe event {Id} ({Type}) already in inbox — ignoring replay.", stripeEvent.Id, stripeEvent.Type);
            return Ok();
        }
        catch (Exception ex)
        {
            // Durable accept failed — return 500 so Stripe retries.
            _logger.LogError(ex, "Failed to persist Stripe event {Id} to webhook inbox.", stripeEvent.Id);
            return StatusCode(500, "Webhook inbox unavailable.");
        }

        var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
        _ = Task.Run(() => HandleStripeEventAsync(stripeEvent, inboxEvent.Id, scopeFactory));
        return Ok();
    }

    // ── Stripe event handler ──────────────────────────────────────────────────

    // REL-003-R: inboxEventId links back to the StripeWebhookEvents row so we can
    // stamp its Status to processed/failed when the handler completes.
    private async Task HandleStripeEventAsync(Event evt, Guid inboxEventId, IServiceScopeFactory scopeFactory)
    {
        string finalStatus;
        string? errorMessage = null;
        try
        {
            switch (evt.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutCompleted(evt, scopeFactory);
                    break;

                case "customer.subscription.updated":
                    await HandleSubscriptionUpdated(evt, scopeFactory);
                    break;

                case "customer.subscription.deleted":
                    await HandleSubscriptionDeleted(evt, scopeFactory);
                    break;

                case "invoice.payment_failed":
                    await HandlePaymentFailed(evt, scopeFactory);
                    break;

                case "invoice.paid":
                    await HandleInvoicePaid(evt, scopeFactory);
                    break;

                default:
                    _logger.LogDebug("Unhandled Stripe event type: {Type}", evt.Type);
                    break;
            }
            finalStatus = "processed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe event {Id} ({Type})", evt.Id, evt.Type);
            finalStatus = "failed";
            errorMessage = ex.Message;
        }

        // Best-effort status update — a failure here does not re-throw; the event
        // row was already durably accepted, and a future recovery sweep can re-process.
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.StripeWebhookEvents.FindAsync(inboxEventId);
            if (row is not null)
            {
                row.Status      = finalStatus;
                row.ProcessedAt = DateTime.UtcNow;
                row.ErrorMessage = errorMessage;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update inbox status for Stripe event {Id}", evt.Id);
        }
    }

    // REL-H2: All handlers now accept IServiceScopeFactory and create their own scope.
    private async Task HandleCheckoutCompleted(Event evt, IServiceScopeFactory scopeFactory)
    {
        if (evt.Data.Object is not Session session) return;
        if (!session.Metadata.TryGetValue("tenantId", out var tenantIdStr)) return;
        if (!Guid.TryParse(tenantIdStr, out var tenantId)) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return;

        var subService = new SubscriptionService();
        var sub = await subService.GetAsync(session.SubscriptionId);

        tenant.StripeCustomerId     = session.CustomerId;
        tenant.StripeSubscriptionId = session.SubscriptionId;
        tenant.BillingStatus        = ResolveBillingStatus(sub.Status);
        tenant.LicenseStart         = sub.CurrentPeriodStart;
        tenant.LicenseEnd           = sub.CurrentPeriodEnd;
        // DC-M1: SubscriptionTier and LicenseCount are obsolete dead columns — removed assignments.
        tenant.PastDueAt            = null;

        await db.SaveChangesAsync();
        _logger.LogInformation("Tenant {TenantId} activated per-device billing.", tenantId);
    }

    private async Task HandleSubscriptionUpdated(Event evt, IServiceScopeFactory scopeFactory)
    {
        if (evt.Data.Object is not Subscription sub) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.StripeSubscriptionId == sub.Id);
        if (tenant is null) return;

        // DC-M1: SubscriptionTier and LicenseCount are obsolete dead columns — removed assignments.
        tenant.LicenseStart     = sub.CurrentPeriodStart;
        tenant.LicenseEnd       = sub.CurrentPeriodEnd;
        tenant.BillingStatus    = ResolveBillingStatus(sub.Status);

        if (tenant.BillingStatus == BillingStatus.Active || tenant.BillingStatus == BillingStatus.Trialing)
            tenant.PastDueAt     = null;
        else if (tenant.BillingStatus == BillingStatus.PastDue)
            tenant.PastDueAt   ??= DateTime.UtcNow;

        await db.SaveChangesAsync();
        _logger.LogInformation("Tenant {TenantId} subscription updated with status {Status}.", tenant.Id, tenant.BillingStatus);
    }

    private async Task HandleSubscriptionDeleted(Event evt, IServiceScopeFactory scopeFactory)
    {
        if (evt.Data.Object is not Subscription sub) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.StripeSubscriptionId == sub.Id);
        if (tenant is null) return;

        tenant.BillingStatus        = BillingStatus.Canceled;
        tenant.StripeSubscriptionId = null;
        tenant.LicenseEnd           = DateTime.UtcNow;

        await db.SaveChangesAsync();
        _logger.LogInformation("Tenant {TenantId} subscription canceled", tenant.Id);
    }

    private async Task HandlePaymentFailed(Event evt, IServiceScopeFactory scopeFactory)
    {
        if (evt.Data.Object is not Invoice invoice) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.StripeCustomerId == invoice.CustomerId);
        if (tenant is null) return;

        tenant.BillingStatus = BillingStatus.PastDue;
        tenant.PastDueAt   ??= DateTime.UtcNow;

        await db.SaveChangesAsync();
        _logger.LogWarning("Tenant {TenantId} payment failed — grace period started", tenant.Id);
    }

    private async Task HandleInvoicePaid(Event evt, IServiceScopeFactory scopeFactory)
    {
        if (evt.Data.Object is not Invoice invoice) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.StripeCustomerId == invoice.CustomerId);
        if (tenant is null) return;

        tenant.BillingStatus = BillingStatus.Active;
        tenant.PastDueAt     = null;

        await db.SaveChangesAsync();
        _logger.LogInformation("Tenant {TenantId} invoice paid — billing restored to Active", tenant.Id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // ARCH-M1: Delegates to shared extension.
    private bool IsAdmin() => User.IsAdmin();

    // ARCH-M1: Delegates to shared extension.
    private bool IsPlatformAdmin() => User.IsPlatformAdmin();

    /// <summary>
    /// AA-M8: Requires a fresh MFA step-up for sensitive platform-admin billing actions.
    /// Returns null when the action may proceed; a 403 otherwise.
    /// </summary>
    private IActionResult? RequireFreshMfaCheck()
        => User.HasFreshMfa(_config)
            ? null
            : StatusCode(403, new
            {
                error = "mfa_required",
                message = "This action requires MFA verification. Verify your authenticator and try again."
            });

    private async Task<string> CreateStripeCustomerAsync(Tenant tenant)
    {
        var customerService = new CustomerService();
        var customer = await customerService.CreateAsync(new CustomerCreateOptions
        {
            Name     = tenant.Name,
            Metadata = new Dictionary<string, string> { ["tenantId"] = tenant.Id.ToString() },
        });
        return customer.Id;
    }

    public record UpdateStripeConfigRequest(
        string? SecretKey,
        string? WebhookSecret,
        string? PerDevicePriceId);

    private static BillingStatus ResolveBillingStatus(string? stripeStatus) => stripeStatus switch
    {
        "trialing" => BillingStatus.Trialing,
        "active" => BillingStatus.Active,
        "canceled" => BillingStatus.Canceled,
        "past_due" or "unpaid" or "incomplete" or "incomplete_expired" => BillingStatus.PastDue,
        _ => BillingStatus.PastDue,
    };
}
