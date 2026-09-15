namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Well-known values for <see cref="OrderNotification.ProviderStatus"/>.
/// The provider owns delivery outcome; these mirror the provider's message-status vocabulary,
/// plus one local value (<see cref="SendError"/>) for the case where the provider never
/// accepted the request at all (so there is no provider record and no message SID).
/// </summary>
public static class NotificationStatus
{
    // Provider lifecycle values.
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>The provider never accepted the create request (network/validation error). No SID exists.</summary>
    public const string SendError = "send_error";

    /// <summary>True once the message has reached a state that will not change on its own.</summary>
    public static bool IsTerminal(string? status) => status is Delivered or Undelivered or Failed or Canceled or SendError;

    /// <summary>True when the message reached the handset.</summary>
    public static bool IsDelivered(string? status) => status is Delivered;

    /// <summary>True when the message did not reach the shopper and a re-send is warranted.</summary>
    public static bool DidNotReach(string? status) => status is Undelivered or Failed or SendError;
}
