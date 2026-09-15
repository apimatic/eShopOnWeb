using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The message delivery status values owned by the messaging provider, as defined by the
/// provider's message schema (message_enum_status). Helpers classify a status without the rest of
/// the application needing to know the individual string values.
/// </summary>
public static class MessageStatus
{
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

    /// <summary>A status from which no further delivery transition is expected.</summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or Received or Read or PartiallyDelivered => true,
        _ => false
    };

    /// <summary>The message reached a state where it did not (and will not) reach the recipient.</summary>
    public static bool IsUndelivered(string? status) => status switch
    {
        Failed or Undelivered => true,
        _ => false
    };

    /// <summary>The message is still scheduled and has not yet been sent, so it can still be called off.</summary>
    public static bool IsScheduled(string? status) =>
        string.Equals(status, Scheduled, StringComparison.OrdinalIgnoreCase);
}
