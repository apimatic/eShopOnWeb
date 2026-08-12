using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known message states. Values mirror the provider's own message status vocabulary so the
/// state the provider owns can be carried faithfully; <see cref="SendFailed"/> is the one local
/// pseudo-status, used when the request to the provider never produced a message (e.g. a transport
/// failure), so there is no provider identifier to report on.
/// </summary>
public static class NotificationStatus
{
    // Provider statuses (api.v2010.account.message `status`).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string Read = "read";
    public const string PartiallyDelivered = "partially_delivered";

    // Local pseudo-status: the send request itself failed before the provider issued an identifier.
    public const string SendFailed = "send_failed";

    /// <summary>
    /// A status is terminal when the provider will not change it further, so it need not be
    /// refreshed from the provider on read.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or Read or SendFailed => true,
        _ => false
    };

    /// <summary>
    /// True when the message is known to have reached the shopper's handset.
    /// </summary>
    public static bool ReachedRecipient(string? status) =>
        string.Equals(status, Delivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Read, StringComparison.OrdinalIgnoreCase);
}
