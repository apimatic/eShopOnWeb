using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The provider's own delivery-status vocabulary, mirrored verbatim so the state the provider
/// owns can be reported without translation. Values map 1:1 to Twilio Message <c>status</c>.
/// </summary>
public static class NotificationStatus
{
    public const string PendingSend = "pending"; // local-only: we have a record but never reached the provider
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Received = "received";
    public const string Read = "read";

    /// <summary>
    /// A status from which no further change is expected, so it need not be re-fetched from the provider.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or Received or Read => true,
        _ => false
    };

    /// <summary>
    /// True when a message did not reach the shopper and is therefore a candidate for an operator re-send.
    /// </summary>
    public static bool IsUndeliverable(string? status) => status switch
    {
        Undelivered or Failed or Canceled => true,
        _ => false
    };
}
