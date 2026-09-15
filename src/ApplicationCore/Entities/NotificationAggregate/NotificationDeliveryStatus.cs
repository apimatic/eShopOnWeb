namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery state of a notification. For sent messages this mirrors the outcome the
/// provider owns (fetched from the provider, since there is no public callback URL for this app).
/// A handful of local-only states cover cases where no provider message exists.
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>Created locally, provider not yet contacted.</summary>
    Pending = 0,

    /// <summary>Deliberately not sent because the shopper has no number on file.</summary>
    NotSent = 1,

    /// <summary>The attempt to hand the message to the provider itself failed (message never created).</summary>
    SendError = 2,

    // ---- states reported by the provider ----
    Queued = 10,
    Sending = 11,
    Sent = 12,
    Delivered = 13,
    Undelivered = 14,
    Failed = 15,
    /// <summary>Accepted by the provider but not yet dispatched.</summary>
    Accepted = 16,
    /// <summary>A scheduled (future) message awaiting its send time.</summary>
    Scheduled = 17,
    /// <summary>A scheduled message that was called off before it went out.</summary>
    Canceled = 18,
    Read = 19,
    PartiallyDelivered = 20
}
