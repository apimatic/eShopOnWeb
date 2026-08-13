using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Delivery-outcome values a notification can carry. Most mirror the provider's own message
/// status; <see cref="Pending"/> and <see cref="SendFailed"/> are local outcomes for the window
/// before, or in place of, a provider acknowledgement.
/// </summary>
public static class NotificationStatus
{
    // Local outcomes
    public const string Pending = "pending";
    public const string SendFailed = "send_failed";

    // Provider statuses we may store verbatim
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";

    /// <summary>Settled outcomes that will not change again, so there is no point re-querying the provider.</summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or SendFailed => true,
        _ => false
    };
}
