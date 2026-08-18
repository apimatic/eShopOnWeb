using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Known notification delivery states. Most values are the provider's own wire status strings; a few
/// are local sentinels for states the provider never reports (e.g. a create call that never reached it).
/// </summary>
public static class NotificationStatus
{
    // Local sentinels.
    public const string Pending = "pending";
    public const string SendFailed = "send_failed";

    // Provider wire statuses (subset that matters to this integration).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>
    /// True when the status will not change again on its own, so there is no point re-polling the provider.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or SendFailed or "received" or "read" => true,
        _ => false
    };
}
