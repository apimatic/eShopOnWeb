namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Maps the provider's own message status wire values onto our <see cref="NotificationStatus"/>. Kept here so
/// the application owns the mapping and the provider integration only ever passes raw wire strings across.
/// </summary>
public static class NotificationStatusMapper
{
    public static NotificationStatus FromProviderStatus(string? wireValue) => wireValue switch
    {
        "queued" => NotificationStatus.Queued,
        "sending" => NotificationStatus.Sending,
        "sent" => NotificationStatus.Sent,
        "delivered" => NotificationStatus.Delivered,
        "undelivered" => NotificationStatus.Undelivered,
        "failed" => NotificationStatus.Failed,
        "scheduled" => NotificationStatus.Scheduled,
        "canceled" => NotificationStatus.Canceled,
        "accepted" => NotificationStatus.Accepted,
        "receiving" => NotificationStatus.Receiving,
        "received" => NotificationStatus.Received,
        "read" => NotificationStatus.Read,
        "partially_delivered" => NotificationStatus.PartiallyDelivered,
        _ => NotificationStatus.Unknown
    };
}
