namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery outcome of a notification. Most values mirror the provider's own message status;
/// two values (<see cref="NoContactNumber"/>, <see cref="SendFailed"/>) describe outcomes the
/// provider never sees because no message was accepted by it.
/// </summary>
public enum NotificationStatus
{
    /// <summary>The shopper had no number on file, so nothing was sent. Not an error.</summary>
    NoContactNumber = 0,

    /// <summary>The provider could not be reached / rejected the request; nothing was accepted.</summary>
    SendFailed = 1,

    /// <summary>Accepted by the provider and queued for immediate sending.</summary>
    Queued = 2,

    /// <summary>Accepted by the provider and scheduled to be sent at a later time.</summary>
    Scheduled = 3,

    Sending = 4,
    Sent = 5,
    Delivered = 6,
    Undelivered = 7,
    Failed = 8,

    /// <summary>A not-yet-sent (scheduled) message that was called off before it went out.</summary>
    Canceled = 9,

    /// <summary>The provider reported a status this app does not have a dedicated mapping for.</summary>
    Unknown = 10
}
