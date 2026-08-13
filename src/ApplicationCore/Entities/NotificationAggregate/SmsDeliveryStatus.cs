using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The provider's message delivery states, kept as the provider's own wire values so a stored
/// notification carries the provider's view verbatim. Also records our own local outcome when a
/// message could not even be handed to the provider (<see cref="SendFailed"/>).
/// </summary>
public static class SmsDeliveryStatus
{
    // Provider wire values (Twilio message status).
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Receiving = "receiving";
    public const string Received = "received";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string PartiallyDelivered = "partially_delivered";

    /// <summary>Local-only outcome: the message never reached the provider (transport/validation failure on our side).</summary>
    public const string SendFailed = "send_failed";

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Delivered, Read, Received, Undelivered, Failed, Canceled, SendFailed
    };

    private static readonly HashSet<string> NotReachedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Undelivered, Failed, Canceled, SendFailed
    };

    /// <summary>A terminal status will never change again, so it need not be re-fetched from the provider.</summary>
    public static bool IsTerminal(string? status) =>
        !string.IsNullOrEmpty(status) && TerminalStates.Contains(status);

    /// <summary>True when the message did not reach the shopper — the states an operator may legitimately re-send.</summary>
    public static bool DidNotReachShopper(string? status) =>
        !string.IsNullOrEmpty(status) && NotReachedStates.Contains(status);
}
