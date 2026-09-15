using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery status of a notification. The values mirror the provider's own message status
/// vocabulary so the state the provider owns is carried verbatim, plus two local-only values for
/// the moments before the provider has a say.
/// </summary>
public static class NotificationStatus
{
    // Local-only states (no provider message exists yet).
    public const string Pending = "pending";       // created, provider not yet called
    public const string SendFailed = "send_failed"; // provider call could not be completed

    // Provider states, carried verbatim from the messaging API.
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    private static readonly HashSet<string> _terminal = new(StringComparer.OrdinalIgnoreCase)
    {
        Delivered, Undelivered, Failed, Canceled
    };

    /// <summary>True once the status can no longer change and no refresh from the provider is needed.</summary>
    public static bool IsTerminal(string status) => status != null && _terminal.Contains(status);

    /// <summary>True when a message reached the shopper (used to decide whether a resend is warranted).</summary>
    public static bool ReachedRecipient(string status) =>
        string.Equals(status, Delivered, StringComparison.OrdinalIgnoreCase);
}
