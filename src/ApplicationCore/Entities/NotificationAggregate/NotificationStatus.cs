using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery-outcome values a <see cref="Notification"/> can carry. Where the value originates
/// from the provider it is stored verbatim (e.g. "queued", "sent", "delivered", "undelivered",
/// "failed", "scheduled", "canceled"); a couple of values are local-only for outcomes that never
/// reached the provider.
/// </summary>
public static class NotificationStatus
{
    // Local-only: recorded before/without a provider round-trip.
    public const string Pending = "pending";
    public const string SendFailed = "send_failed";

    // Provider statuses we care about (stored verbatim as returned by Twilio).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Delivered, Undelivered, Failed, Canceled, SendFailed
    };

    /// <summary>
    /// True when the status will not change again and there is no value in re-querying the provider.
    /// </summary>
    public static bool IsTerminal(string? status) =>
        status is not null && TerminalStatuses.Contains(status);
}
