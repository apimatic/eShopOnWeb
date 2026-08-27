namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Well-known delivery status values reported by the messaging provider,
/// plus local statuses used when the provider never accepted the message.
/// </summary>
public static class OrderNotificationStatuses
{
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>Local status: the provider rejected the send request outright.</summary>
    public const string Rejected = "rejected";
}
