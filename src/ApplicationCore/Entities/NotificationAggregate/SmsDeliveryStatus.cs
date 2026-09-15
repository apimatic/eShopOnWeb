using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery outcome of a message, as owned by the messaging provider. Values mirror the
/// provider's Message status vocabulary (from the Twilio OpenAPI <c>message_enum_status</c>),
/// plus two local sentinels for states that exist before the provider has a record.
/// </summary>
public static class SmsDeliveryStatus
{
    // Local sentinels (never returned by the provider).
    /// <summary>Raised locally but not yet handed to the provider.</summary>
    public const string Pending = "pending";
    /// <summary>The provider call to create the message failed; nothing was accepted.</summary>
    public const string SendFailed = "send_failed";

    // Provider-owned statuses (Twilio message_enum_status).
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Receiving = "receiving";
    public const string Received = "received";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Read = "read";
    public const string PartiallyDelivered = "partially_delivered";
    public const string Canceled = "canceled";

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Delivered, Undelivered, Failed, Canceled, Received, Read, SendFailed
    };

    /// <summary>
    /// True when the outcome will not change again, so there is no reason to ask the provider
    /// for a fresh status.
    /// </summary>
    public static bool IsTerminal(string? status) =>
        !string.IsNullOrEmpty(status) && TerminalStatuses.Contains(status);

    /// <summary>
    /// True when the message did not reach the shopper and is therefore a candidate for re-send.
    /// </summary>
    public static bool IsUndeliverable(string? status) =>
        !string.IsNullOrEmpty(status) &&
        (string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(status, Undelivered, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(status, SendFailed, StringComparison.OrdinalIgnoreCase));
}
