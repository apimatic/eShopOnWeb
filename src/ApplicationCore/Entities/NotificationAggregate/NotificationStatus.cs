namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Well-known values for <see cref="OrderNotification.Status"/>.
/// Most values mirror the provider's own message status verbatim (queued, sending, sent,
/// delivered, undelivered, failed, scheduled, canceled, accepted). Two extra values describe
/// states the provider never owns: a notification that has not been handed to the provider yet
/// (<see cref="Pending"/>) and one whose hand-off threw before a provider id came back
/// (<see cref="SendFailed"/>).
/// </summary>
public static class NotificationStatus
{
    /// <summary>Created locally, not yet accepted by the provider.</summary>
    public const string Pending = "pending";

    /// <summary>The attempt to hand the message to the provider failed (no provider id obtained).</summary>
    public const string SendFailed = "send_failed";

    // Provider-owned terminal delivery outcomes.
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>
    /// True when the status is one the provider will no longer change on its own, so there is no
    /// value in fetching it again.
    /// </summary>
    public static bool IsTerminal(string? status) =>
        status is Delivered or Undelivered or Failed or Canceled or SendFailed;
}
