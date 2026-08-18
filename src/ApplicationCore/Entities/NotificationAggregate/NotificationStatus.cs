namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known values for <see cref="OrderNotification.Status"/>. The provider's own
/// delivery statuses (queued, sending, sent, delivered, undelivered, failed, scheduled,
/// canceled, accepted, …) are stored verbatim as their lower-case wire strings; the two
/// constants below are the app-local outcomes that have no provider message behind them.
/// </summary>
public static class NotificationStatus
{
    /// <summary>The send was attempted but the provider rejected the request outright (no message SID).</summary>
    public const string SendFailed = "send_failed";

    /// <summary>Scheduling the follow-up with the provider was rejected (no scheduled message exists).</summary>
    public const string ScheduleFailed = "schedule_failed";
}
