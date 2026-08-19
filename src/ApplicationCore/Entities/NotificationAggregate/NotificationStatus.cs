using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Delivery-status vocabulary. The values mirror Twilio's Message <c>status</c> enum
/// (from the OpenAPI spec) so we can store the provider's own outcome verbatim, plus a
/// single local <see cref="Pending"/> value for a notification that has not been handed
/// to the provider yet.
/// </summary>
public static class NotificationStatus
{
    // Local-only: created but not yet accepted by the provider.
    public const string Pending = "pending";

    // Provider (Twilio) statuses.
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Receiving = "receiving";
    public const string Received = "received";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Read = "read";
    public const string PartiallyDelivered = "partially_delivered";
    public const string Canceled = "canceled";

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Delivered, Undelivered, Failed, Canceled, Read
    };

    private static readonly HashSet<string> UndeliveredStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Undelivered, Failed
    };

    /// <summary>
    /// True once the outcome is settled and will not change on its own — no point
    /// re-fetching it from the provider.
    /// </summary>
    public static bool IsTerminal(string? status) =>
        status is not null && TerminalStatuses.Contains(status);

    /// <summary>True when the message failed to reach the recipient (resend-eligible).</summary>
    public static bool IsUndelivered(string? status) =>
        status is not null && UndeliveredStatuses.Contains(status);

    /// <summary>True when the message reached, or is genuinely on its way to, the recipient.</summary>
    public static bool HasReachedRecipient(string? status) =>
        string.Equals(status, Delivered, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Read, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Sent, StringComparison.OrdinalIgnoreCase);
}
