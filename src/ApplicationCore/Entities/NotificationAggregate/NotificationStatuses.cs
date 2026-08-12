namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Local status markers used when there is no provider status to record. Once a message has a
/// provider message id, <see cref="OrderNotification.Status"/> holds the provider's own wire
/// status verbatim (queued, sent, delivered, undelivered, failed, scheduled, canceled, ...).
/// </summary>
public static class NotificationStatuses
{
    /// <summary>The message has not yet been handed to the provider.</summary>
    public const string Pending = "pending";

    /// <summary>The provider rejected the create call outright — nothing was queued.</summary>
    public const string SendFailed = "send_failed";

    /// <summary>A scheduled follow-up that this application cancelled with the provider before it went out.</summary>
    public const string Canceled = "canceled";
}
