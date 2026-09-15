namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery outcome a message can carry. Values mirror the provider's own message
/// status vocabulary so that a status read back from the provider can be stored verbatim,
/// plus a single local sentinel (<see cref="SendError"/>) for the case where the provider
/// call itself never produced a message.
/// </summary>
public static class MessageDeliveryStatus
{
    // Provider-owned statuses (Twilio Message resource "status").
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    // Local sentinel: the provider never accepted the request, so there is no message to track.
    public const string SendError = "send_error";

    /// <summary>A status that will not change on its own, so it need not be refreshed from the provider.</summary>
    public static bool IsTerminal(string? status) =>
        status is Delivered or Undelivered or Failed or Canceled or SendError;

    /// <summary>The message reached the handset.</summary>
    public static bool ReachedHandset(string? status) => status is Delivered;

    /// <summary>The message did not reach the shopper and is therefore a candidate for a re-send.</summary>
    public static bool DidNotReachHandset(string? status) =>
        status is Undelivered or Failed or SendError;

    /// <summary>The message is queued with the provider for a future send and can still be called off.</summary>
    public static bool IsScheduled(string? status) => status is Scheduled;
}
