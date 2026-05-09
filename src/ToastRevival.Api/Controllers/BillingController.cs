using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class BillingController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILicenseService _license;
    private readonly IConfiguration _config;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        AppDbContext db,
        ILicenseService license,
        IConfiguration config,
        ILogger<BillingController> logger)
    {
        _db = db;
        _license = license;
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

        var limit = tenant.LicenseCount;
        var consumed = tenant.ConsumedCount;
        var isNearLimit  = limit > 0 && consumed >= (int)(limit * 0.9);
        var isAtLimit    = limit > 0 && consumed >= limit;

        return Ok(new
        {
            tier             = tenant.SubscriptionTier.ToString(),
            tierLabel        = _license.GetTierLabel(tenant.SubscriptionTier),
            licenseCount     = limit,
            deviceLimit      = limit == 0 ? (int?)null : limit,
            consumedCount    = consumed,
            billingStatus    = tenant.BillingStatus.ToString(),
            licenseStart     = tenant.LicenseStart,
            licenseEnd       = tenant.LicenseEnd,
            stripeCustomerId = tenant.StripeCustomerId,
            isNearLimit,
            isAtLimit,
        });
    }

    // ── POST /api/billing/checkout ────────────────────────────────────────────

    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout([FromBody] CreateCheckoutRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return NotFound();

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.StartsWith("sk_test_REPLACE"))
            return StatusCode(503, "Billing is not configured on this server.");

        var priceId = req.Tier switch
        {
            "Pro"        => _config["Stripe:ProPriceId"],
            "Enterprise" => _config["Stripe:EnterprisePriceId"],
            _            => null
        };
        if (string.IsNullOrWhiteSpace(priceId))
            return BadRequest("Invalid tier. Valid values: Pro, Enterprise.");

        StripeConfiguration.ApiKey = secretKey;

        // Ensure Stripe customer exists
        string customerId = tenant.StripeCustomerId ?? await CreateStripeCustomerAsync(tenant);
        if (string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
        {
            tenant.StripeCustomerId = customerId;
            await _db.SaveChangesAsync();
        }

        var sessionService = new SessionService();
        var sessionOpts = new SessionCreateOptions
        {
            Customer = customerId,
            Mode     = "subscription",
            LineItems =
            [
                new SessionLineItemOptions { Price = priceId, Quantity = 1 }
            ],
            SuccessUrl = _config["Stripe:SuccessUrl"] ?? "http://localhost:5173/billing?session=success",
            CancelUrl  = _config["Stripe:CancelUrl"]  ?? "http://localhost:5173/billing",
            Metadata   = new Dictionary<string, string> { ["tenantId"] = tenantId.ToString() },
        };

        var session = await sessionService.CreateAsync(sessionOpts);
        return Ok(new { url = session.Url });
    }

    // ── POST /api/billing/portal ──────────────────────────────────────────────

    [HttpPost("portal")]
    public async Task<IActionResult> CreatePortal()
    {
        if (!IsAdmin()) return Forbid();

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

        var portalSession = await portalService.CreateAsync(portalOpts);
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
            return Ok(new { invoices = Array.Empty<object>() });

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.StartsWith("sk_test_REPLACE"))
            return Ok(new { invoices = Array.Empty<object>() });

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
    public async Task<IActionResult> Webhook()
    {
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

        // Capture the root IServiceProvider before returning — HttpContext is recycled
        // after the response is sent, but the root provider outlives the request.
        var services = HttpContext.RequestServices;
        _ = Task.Run(() => HandleStripeEventAsync(stripeEvent, services));
        return Ok();
    }

    // ── Stripe event handler ──────────────────────────────────────────────────

    private async Task HandleStripeEventAsync(Event evt, IServiceProvider services)
    {
        try
        {
            switch (evt.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutCompleted(evt, services);
                    break;

                case "customer.subscription.updated":
                    await HandleSubscriptionUpdated(evt, services);
                    break;

                case "customer.subscription.deleted":
                    await HandleSubscriptionDeleted(evt, services);
                    break;

                case "invoice.payment_failed":
                    await HandlePaymentFailed(evt, services);
                    break;

                case "invoice.paid":
                    await HandleInvoicePaid(evt, services);
                    break;

                default:
                    _logger.LogDebug("Unhandled Stripe event type: {Type}", evt.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe event {Id} ({Type})", evt.Id, evt.Type);
        }
    }

    private async Task HandleCheckoutCompleted(Event evt, IServiceProvider services)
    {
        if (evt.Data.Object is not Session session) return;
        if (!session.Metadata.TryGetValue("tenantId", out var tenantIdStr)) return;
        if (!Guid.TryParse(tenantIdStr, out var tenantId)) return;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return;

        var subService = new SubscriptionService();
        var sub = await subService.GetAsync(session.SubscriptionId);

        tenant.StripeCustomerId     = session.CustomerId;
        tenant.StripeSubscriptionId = session.SubscriptionId;
        tenant.BillingStatus        = BillingStatus.Active;
        tenant.LicenseStart         = sub.CurrentPeriodStart;
        tenant.LicenseEnd           = sub.CurrentPeriodEnd;
        tenant.SubscriptionTier     = ResolveTier(sub);
        tenant.LicenseCount         = ResolveLicenseCount(tenant.SubscriptionTier);
        tenant.PastDueAt            = null;

        await db.SaveChangesAsync();
        _logger.LogInformation("Tenant {TenantId} subscribed to {Tier}", tenantId, tenant.SubscriptionTier);
    }

    private async Task HandleSubscriptionUpdated(Event evt, IServiceProvider services)
    {
        if (evt.Data.Object is not Subscription sub) return;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.StripeSubscriptionId == sub.Id);
        if (tenant is null) return;

        tenant.SubscriptionTier = ResolveTier(sub);
        tenant.LicenseCount     = ResolveLicenseCount(tenant.SubscriptionTier);
        tenant.LicenseStart     = sub.CurrentPeriodStart;
        tenant.LicenseEnd       = sub.CurrentPeriodEnd;

        if (sub.Status == "active" || sub.Status == "trialing")
        {
            tenant.BillingStatus = sub.Status == "trialing" ? BillingStatus.Trialing : BillingStatus.Active;
            tenant.PastDueAt     = null;
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Tenant {TenantId} subscription updated to {Tier}", tenant.Id, tenant.SubscriptionTier);
    }

    private async Task HandleSubscriptionDeleted(Event evt, IServiceProvider services)
    {
        if (evt.Data.Object is not Subscription sub) return;

        using var scope = services.CreateScope();
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

    private async Task HandlePaymentFailed(Event evt, IServiceProvider services)
    {
        if (evt.Data.Object is not Invoice invoice) return;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.StripeCustomerId == invoice.CustomerId);
        if (tenant is null) return;

        tenant.BillingStatus = BillingStatus.PastDue;
        tenant.PastDueAt   ??= DateTime.UtcNow;

        await db.SaveChangesAsync();
        _logger.LogWarning("Tenant {TenantId} payment failed — grace period started", tenant.Id);
    }

    private async Task HandleInvoicePaid(Event evt, IServiceProvider services)
    {
        if (evt.Data.Object is not Invoice invoice) return;

        using var scope = services.CreateScope();
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

    private bool IsAdmin()
    {
        var role = Enum.TryParse<UserRole>(User.FindFirstValue("role"), out var r) ? r : UserRole.Technician;
        return role >= UserRole.Admin;
    }

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

    private static SubscriptionTier ResolveTier(Subscription sub)
    {
        // Map Stripe plan nickname or metadata to our tier enum.
        // Fallback: infer from price amount.
        var nickname = sub.Items?.Data?.FirstOrDefault()?.Price?.Nickname?.ToLower() ?? "";
        if (nickname.Contains("enterprise")) return SubscriptionTier.Enterprise;
        if (nickname.Contains("pro")) return SubscriptionTier.Pro;
        return SubscriptionTier.Pro; // default paid = Pro
    }

    private static int ResolveLicenseCount(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.Free       => 10,
        SubscriptionTier.Pro        => 250,
        SubscriptionTier.Enterprise => 0,   // 0 = unlimited
        _                           => 10,
    };
}

public record CreateCheckoutRequest(string Tier);
