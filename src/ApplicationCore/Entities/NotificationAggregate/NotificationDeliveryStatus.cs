namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery outcome of a single message, mirroring the provider's own message status so that a
/// later request can act on it and report on it. <see cref="PendingSend"/> is the only value the
/// provider does not own: it marks a notification recorded before the provider ever accepted it
/// (for example when the send call itself threw).
/// </summary>
public enum NotificationDeliveryStatus
{
    // Local-only: recorded but not yet handed to the provider.
    PendingSend = 0,

    // Provider-owned lifecycle (names align with the provider's message status values).
    Queued = 1,
    Sending = 2,
    Sent = 3,
    Delivered = 4,
    Undelivered = 5,
    Failed = 6,
    Accepted = 7,
    Scheduled = 8,
    Canceled = 9,
    PartiallyDelivered = 10,
    Read = 11,
    Receiving = 12,
    Received = 13,

    // The provider returned a status this integration does not recognise.
    Unknown = 99
}
