using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The delivery-outcome values Twilio reports for a message, plus a local-only value used when the
/// provider call itself never completed. Kept as strings because the outcome is owned by the provider.
/// </summary>
public static class MessageStatuses
{
    // Twilio message statuses.
    public const string Accepted = "accepted";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";

    /// <summary>Local-only: the provider API call did not complete, so no message was created.</summary>
    public const string SendError = "send_error";

    /// <summary>A message that will not change any further and did not reach the shopper.</summary>
    public static bool IsFailure(string? status) =>
        string.Equals(status, Undelivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, SendError, StringComparison.OrdinalIgnoreCase);

    /// <summary>A message whose outcome is settled and will not change on a later poll.</summary>
    public static bool IsTerminal(string? status) =>
        IsFailure(status) ||
        string.Equals(status, Delivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Canceled, StringComparison.OrdinalIgnoreCase);
}
