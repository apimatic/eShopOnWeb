namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Translates the provider's message status strings into eShop's <see cref="NotificationStatus"/>,
/// and answers whether a status is terminal (no further transitions expected).
/// </summary>
public static class SmsStatusMapper
{
    public static NotificationStatus Map(string? providerStatus)
    {
        return providerStatus?.Trim().ToLowerInvariant() switch
        {
            "queued" => NotificationStatus.Queued,
            "accepted" => NotificationStatus.Accepted,
            "scheduled" => NotificationStatus.Scheduled,
            "sending" => NotificationStatus.Sending,
            "sent" => NotificationStatus.Sent,
            "delivered" => NotificationStatus.Delivered,
            "undelivered" => NotificationStatus.Undelivered,
            "failed" => NotificationStatus.Failed,
            "canceled" => NotificationStatus.Canceled,
            "cancelled" => NotificationStatus.Canceled,
            "read" => NotificationStatus.Read,
            "partially_delivered" => NotificationStatus.Delivered,
            "receiving" => NotificationStatus.Unknown,
            "received" => NotificationStatus.Unknown,
            null => NotificationStatus.Unknown,
            "" => NotificationStatus.Unknown,
            _ => NotificationStatus.Unknown
        };
    }

    /// <summary>
    /// A terminal status will not change again, so a fresh read from the provider is unnecessary.
    /// </summary>
    public static bool IsTerminal(NotificationStatus status)
    {
        return status is NotificationStatus.Delivered
            or NotificationStatus.Undelivered
            or NotificationStatus.Failed
            or NotificationStatus.Canceled
            or NotificationStatus.Read
            or NotificationStatus.SendError;
    }
}
