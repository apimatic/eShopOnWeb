namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Local synthetic delivery-status values used when the outcome is not (yet) a provider status.
/// Once the provider accepts a message, <see cref="OrderNotification.DeliveryStatus"/> instead
/// holds the provider's own wire status (queued, sending, sent, delivered, undelivered, failed,
/// scheduled, canceled, ...).
/// </summary>
public static class NotificationDeliveryStatus
{
    /// <summary>Created locally; not yet handed to the provider.</summary>
    public const string Pending = "pending";

    /// <summary>The send could not be handed to the provider at all (network/API error).</summary>
    public const string SendFailed = "send_failed";

    /// <summary>A scheduled message that was called off before it went out.</summary>
    public const string Canceled = "canceled";
}
