using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Known provider delivery-status values. The provider's own string is always stored verbatim on a
/// <see cref="Notification"/>; these constants exist so orchestration can reason about terminality and
/// which messages did not reach the shopper without magic strings.
/// </summary>
public static class MessageStatuses
{
    // Local-only placeholder before the provider has accepted anything.
    public const string Pending = "pending";

    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Delivered, Undelivered, Failed, Canceled
    };

    private static readonly HashSet<string> DidNotReachStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Undelivered, Failed, Canceled
    };

    /// <summary>A status the provider will not move on from — no point re-polling it.</summary>
    public static bool IsTerminal(string? status) =>
        status != null && TerminalStatuses.Contains(status);

    /// <summary>A message that did not reach the shopper and is therefore eligible for an operator resend.</summary>
    public static bool DidNotReach(string? status) =>
        status != null && DidNotReachStatuses.Contains(status);
}
