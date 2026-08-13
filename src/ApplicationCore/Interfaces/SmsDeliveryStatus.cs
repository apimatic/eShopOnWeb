using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Delivery outcomes eShop tracks for an SMS. The values that come from the provider mirror
/// Twilio's message <c>status</c> field verbatim; a few local-only markers cover states that exist
/// before or without a provider round-trip.
/// </summary>
public static class SmsDeliveryStatus
{
    // Local-only markers (no provider round-trip yet, or the send never reached the provider).
    public const string Pending = "pending";
    public const string SendFailed = "send_failed";
    public const string Unknown = "unknown";

    // Provider (Twilio) status values.
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>
    /// True when the message definitively did not reach the shopper and is therefore a candidate
    /// for an operator resend.
    /// </summary>
    public static bool DidNotReach(string? status) =>
        string.Equals(status, Undelivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, SendFailed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Canceled, StringComparison.OrdinalIgnoreCase);
}
