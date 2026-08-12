using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The provider's delivery-status vocabulary for a message, plus one local value
/// (<see cref="NotSent"/>) for the case where the send request never yielded a provider
/// identifier at all (e.g. a transport error). Stored as a string so the provider can add
/// values without a schema change.
/// </summary>
public static class MessageDeliveryStatus
{
    // Local-only: the provider never accepted the message, so there is no SID to track.
    public const string NotSent = "not_sent";

    // Provider lifecycle values.
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Read = "read";

    /// <summary>
    /// True once the outcome can no longer change, so there is no point re-fetching it from
    /// the provider. <see cref="NotSent"/> is treated as terminal because no SID exists to poll.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or Read or NotSent => true,
        _ => false
    };

    /// <summary>
    /// True when a message did not reach the shopper and is therefore a candidate for resend.
    /// </summary>
    public static bool IsUndelivered(string? status) =>
        string.Equals(status, Undelivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, NotSent, StringComparison.OrdinalIgnoreCase);
}
