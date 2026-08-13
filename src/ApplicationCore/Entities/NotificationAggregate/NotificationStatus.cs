using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Status values a <see cref="Notification"/> can hold. Provider delivery states (queued,
/// sent, delivered, undelivered, failed, scheduled, canceled, ...) are stored verbatim as
/// returned by the messaging provider. A small set of application-only sentinels below cover
/// outcomes that never reached the provider at all.
/// </summary>
public static class NotificationStatus
{
    /// <summary>Nothing was sent because the shopper has no number on file.</summary>
    public const string NoContactNumber = "no_contact_number";

    /// <summary>The provider rejected or errored on the send request; no message went out.</summary>
    public const string SendFailed = "send_failed";

    // Provider delivery states we care about (kept for reference / comparisons).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";
    public const string Accepted = "accepted";

    /// <summary>
    /// States that are final: no further provider polling can change them, so a scheduled
    /// follow-up in one of these states can no longer be called off.
    /// </summary>
    private static readonly HashSet<string> _terminal = new(StringComparer.OrdinalIgnoreCase)
    {
        Delivered, Undelivered, Failed, Canceled, NoContactNumber, SendFailed
    };

    public static bool IsTerminal(string? status) =>
        status is not null && _terminal.Contains(status);
}
