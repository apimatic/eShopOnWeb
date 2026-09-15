namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery-outcome string values stored on <see cref="OrderNotification.DeliveryStatus"/>.
/// The provider's own status is stored verbatim once known; the remaining values are app-level
/// sentinels for states that exist before, or instead of, a provider status.
/// </summary>
public static class NotificationDeliveryStatus
{
    // App-level sentinels.
    /// <summary>Created locally, not yet handed to the provider.</summary>
    public const string Pending = "pending";
    /// <summary>The provider would not accept the message (e.g. a request error). No SID exists.</summary>
    public const string SendFailed = "send_failed";

    // Provider status values (stored verbatim from the Message resource).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string Accepted = "accepted";
}
