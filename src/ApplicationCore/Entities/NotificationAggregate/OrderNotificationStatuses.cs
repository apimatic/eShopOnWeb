namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery outcomes as reported by the provider (Twilio message statuses),
/// plus local bookkeeping values.
/// </summary>
public static class OrderNotificationStatuses
{
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>Local status when the provider never accepted the message.</summary>
    public const string SendFailed = "send_failed";

    public static readonly string[] Final =
    {
        Delivered, Undelivered, Failed, Canceled, SendFailed
    };

    public static bool IsFinal(string status) => System.Array.IndexOf(Final, status) >= 0;
}
