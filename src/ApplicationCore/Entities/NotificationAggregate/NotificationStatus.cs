namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Provider message statuses (wire values) plus the local SendFailed marker.
/// </summary>
public static class NotificationStatus
{
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string SendFailed = OrderNotification.SendFailedStatus;

    /// <summary>Statuses after which no further provider-side change is expected.</summary>
    public static bool IsTerminal(string status) =>
        status is Delivered or Undelivered or Failed or Canceled or SendFailed;
}
