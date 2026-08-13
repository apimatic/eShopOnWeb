namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Local delivery-status markers used when the provider has no status of its own to report yet
/// (before a send, or when a send/cancel could not be completed). Once the provider owns the
/// message, its own wire status value (e.g. "queued", "sent", "delivered", "undelivered",
/// "failed", "scheduled", "canceled") is stored verbatim instead.
/// </summary>
public static class NotificationDeliveryStatus
{
    /// <summary>Created locally, not yet handed to the provider.</summary>
    public const string Pending = "pending";

    /// <summary>The provider rejected the send, or could not be reached, at send time.</summary>
    public const string SendFailed = "send_failed";

    /// <summary>A scheduled message could not be cancelled at the provider.</summary>
    public const string CancelFailed = "cancel_failed";
}
