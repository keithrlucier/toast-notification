namespace ToastRevival.Api.Models;

/// <summary>
/// REL-003-R: Durable inbox for Stripe webhook events. An event row is inserted
/// BEFORE returning 2xx to Stripe so a crash-after-ack can never lose the event.
/// EventId (Stripe's globally-unique evt_xxx identifier) carries a UNIQUE index
/// to guarantee exactly-once processing on Stripe retries.
/// </summary>
public class StripeWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stripe's evt_xxx ID — unique idempotency key.</summary>
    public string EventId { get; set; } = "";

    public string EventType { get; set; } = "";

    /// <summary>received | processing | processed | failed</summary>
    public string Status { get; set; } = "received";

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
