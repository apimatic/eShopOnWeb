namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Known delivery-status strings. Provider (Twilio) lifecycle values are stored verbatim; a couple of
/// local-only values cover states the provider never reached (a submission that the app itself could not
/// hand off). Control flow branches on these strings rather than on transport error codes.
/// </summary>
public static class MessageDeliveryStatus
{
    // Local-only: the message was recorded but the app has not (yet) handed it to the provider.
    public const string Pending = "pending";

    // Local-only: the provider rejected the create call, so nothing was ever queued.
    public const string SubmissionFailed = "submission_failed";

    // Provider lifecycle values (stored verbatim from Twilio).
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>
    /// A status the provider will not move on from, so there is no value in polling it again.
    /// </summary>
    public static bool IsTerminal(string? status) => status switch
    {
        Delivered or Undelivered or Failed or Canceled or SubmissionFailed => true,
        _ => false
    };
}
