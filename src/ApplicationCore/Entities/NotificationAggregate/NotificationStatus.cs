namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Normalized delivery-state values an <see cref="OrderNotification"/> can hold. These mirror the
/// provider's own message statuses, plus a couple of local-only states for messages that never
/// reached the provider. The value the provider currently reports is authoritative and is refreshed
/// from the provider when notifications are read.
/// </summary>
public static class NotificationStatus
{
    // Provider-owned states (lower-cased provider status strings).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Scheduled = "scheduled";

    // Local-only state: the provider was never successfully asked to send this message.
    public const string SendError = "send_error";
}
