namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// The last-known status of a notification. Most values are the messaging provider's own delivery
/// status (its wire value, e.g. <c>queued</c>, <c>sent</c>, <c>delivered</c>, <c>undelivered</c>,
/// <c>failed</c>, <c>scheduled</c>, <c>canceled</c>) stored verbatim so a later request can act on
/// and report the state the provider owns. The two constants below are app-level states that exist
/// before, or in place of, a provider status.
/// </summary>
public static class NotificationDeliveryStatus
{
    /// <summary>The notification record exists but has not yet been handed to the provider.</summary>
    public const string Pending = "pending";

    /// <summary>The provider never accepted the message (rejected or unreachable); there is no provider SID.</summary>
    public const string FailedToSend = "failed_to_send";

    // Provider delivery statuses we treat as terminal (no point re-polling the provider for them).
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Read = "read";
    public const string Scheduled = "scheduled";

    /// <summary>
    /// True when the status will not change on its own, so there is no value in polling the provider again.
    /// (<see cref="Scheduled"/> is deliberately non-terminal: a scheduled message is still expected to move.)
    /// </summary>
    public static bool IsTerminal(string? status) => status is
        Delivered or Undelivered or Failed or Canceled or Read or FailedToSend;
}
