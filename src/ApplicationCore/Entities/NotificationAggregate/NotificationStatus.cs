using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known delivery statuses for a notification. Most values mirror the provider's own
/// Message status vocabulary (queued, sending, sent, delivered, undelivered, failed,
/// canceled, scheduled, accepted, read). Two extra sentinels describe outcomes that never
/// reached the provider at all.
/// </summary>
public static class NotificationStatus
{
    // Provider-owned statuses (see Twilio Message resource `status`).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Scheduled = "scheduled";
    public const string Accepted = "accepted";
    public const string Sending2 = "receiving";
    public const string Read = "read";

    /// <summary>The shopper had no number on file, so nothing was sent.</summary>
    public const string NotSent = "not_sent";

    /// <summary>The provider call to send the message threw before a message was created.</summary>
    public const string SendFailed = "send_failed";

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Delivered, Undelivered, Failed, Canceled, Read, NotSent, SendFailed
    };

    /// <summary>
    /// True when the status will not change again, so there is no value in re-fetching it
    /// from the provider.
    /// </summary>
    public static bool IsTerminal(string? status) =>
        status is not null && TerminalStatuses.Contains(status);
}
