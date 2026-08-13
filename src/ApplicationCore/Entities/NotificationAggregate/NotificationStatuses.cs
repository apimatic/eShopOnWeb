namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known values for <see cref="Notification.Status"/>. When a message is accepted by the
/// provider the status mirrors the provider's own delivery status verbatim (e.g. queued, sent,
/// delivered, undelivered, failed, scheduled, canceled). The constants below cover the states
/// that are local to this application because the provider never saw the message.
/// </summary>
public static class NotificationStatuses
{
    /// <summary>A notification record that has been created but not yet dispatched to the provider.</summary>
    public const string Pending = "pending";

    /// <summary>The message could not be handed to the provider (rejected or provider unreachable); it never reached the shopper.</summary>
    public const string SendFailed = "send_failed";

    /// <summary>A future message queued with the provider (mirrors the provider's "scheduled" status).</summary>
    public const string Scheduled = "scheduled";

    /// <summary>A scheduled message called off before it was sent (mirrors the provider's "canceled" status).</summary>
    public const string Canceled = "canceled";
}
