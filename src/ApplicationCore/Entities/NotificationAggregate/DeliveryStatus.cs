using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery-status vocabulary the messaging provider owns, plus two local states used when the
/// provider never created a resource. Stored verbatim as a string so a later request can act on and
/// report the current outcome. Classification here follows the provider's documented lifecycle:
/// <c>queued/accepted/sending/sent</c> are in-flight, <c>scheduled</c> is a future send,
/// and <c>delivered/undelivered/failed/canceled</c> are terminal.
/// </summary>
public static class DeliveryStatus
{
    // Provider states (see the provider's message lifecycle).
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Read = "read";

    /// <summary>Local state: the send was never attempted because the caller had no number on file.</summary>
    public const string NotSent = "not_sent";

    /// <summary>A terminal state will not change on its own; there is no point re-reading it.</summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or NotSent => true,
        _ => false
    };

    /// <summary>True once the provider confirms the message reached the handset.</summary>
    public static bool ReachedRecipient(string? status) =>
        status is Delivered or Read;

    /// <summary>
    /// True when the message reached a terminal state without being delivered — the case an operator
    /// re-send is for.
    /// </summary>
    public static bool DidNotReachRecipient(string? status) =>
        status is Undelivered or Failed;
}
