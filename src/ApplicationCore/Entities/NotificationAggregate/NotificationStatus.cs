using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery-status values this integration mirrors from the provider, plus one local-only
/// value (<see cref="NotSent"/>) for the case where the provider never accepted the request at all.
/// Status is stored as a string so it always reflects exactly what the provider owns.
/// </summary>
public static class NotificationStatus
{
    // Local-only: the create call itself failed (network/4xx), so there is no provider record.
    public const string NotSent = "not_sent";

    // Provider lifecycle values (see the provider's delivery-status documentation).
    public const string Scheduled = "scheduled";
    public const string Accepted = "accepted";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>
    /// True once the message has reached an outcome that will not change, so there is no point
    /// re-fetching it from the provider.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or NotSent => true,
        _ => false
    };

    /// <summary>True when the shopper did not receive the message — the case an operator resend is for.</summary>
    public static bool IsUndeliveredOutcome(string? status) => status switch
    {
        Undelivered or Failed or NotSent => true,
        _ => false
    };
}
