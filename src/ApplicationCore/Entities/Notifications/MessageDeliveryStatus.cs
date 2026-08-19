using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// The delivery status values the messaging provider owns for a message, mirrored from the
/// Twilio message resource status enum in the OpenAPI spec, plus one local value for the case
/// where the provider could not be reached at all so no provider status ever existed.
/// </summary>
public static class MessageDeliveryStatus
{
    // Provider-owned statuses (Twilio message_enum_status).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Read = "read";
    public const string Canceled = "canceled";
    public const string PartiallyDelivered = "partially_delivered";

    /// <summary>
    /// Local-only status: the provider call to send the message failed outright (e.g. network
    /// error), so the message was never accepted and has no provider SID or status. Recorded so
    /// the notification history still reflects that a send was attempted and did not go out.
    /// </summary>
    public const string NotSent = "not_sent";

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Delivered, Undelivered, Failed, Canceled, Read, PartiallyDelivered, NotSent
    };

    private static readonly HashSet<string> DidNotReachStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Failed, Undelivered, Canceled, NotSent
    };

    /// <summary>Whether the status is final and will not change (no point re-polling the provider).</summary>
    public static bool IsTerminal(string? status) => status is not null && TerminalStatuses.Contains(status);

    /// <summary>Whether the message did not reach the shopper and is therefore eligible for re-send.</summary>
    public static bool DidNotReach(string? status) => status is not null && DidNotReachStatuses.Contains(status);
}
