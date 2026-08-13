using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The provider (Twilio) message status values this integration reasons about, plus a local
/// <see cref="NotSent"/> sentinel for the case where the provider never accepted the message.
/// See https://www.twilio.com/docs/messaging/api/message-resource#message-status-values.
/// </summary>
public static class MessageDeliveryStatus
{
    // Local-only: the provider was never successfully asked to send (e.g. the send call threw).
    public const string NotSent = "not_sent";

    // In-flight / non-terminal provider states.
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";

    // Terminal provider states.
    public const string Sent = "sent";              // left Twilio; delivery not confirmed
    public const string Delivered = "delivered";    // confirmed on the handset
    public const string Undelivered = "undelivered";// carrier refused
    public const string Failed = "failed";          // could not be sent
    public const string Canceled = "canceled";      // a scheduled message called off before send

    /// <summary>True once the provider will report no further transitions for this status.</summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled => true,
        _ => false
    };

    /// <summary>
    /// True when the message is known to have failed to reach the handset — the outcome that makes
    /// a message eligible for an operator re-send.
    /// </summary>
    public static bool DidNotReachHandset(string? status) => status switch
    {
        Undelivered or Failed or Canceled or NotSent => true,
        _ => false
    };

    /// <summary>True when the handset is confirmed to have received the message.</summary>
    public static bool ReachedHandset(string? status) =>
        string.Equals(status, Delivered, StringComparison.OrdinalIgnoreCase);
}
