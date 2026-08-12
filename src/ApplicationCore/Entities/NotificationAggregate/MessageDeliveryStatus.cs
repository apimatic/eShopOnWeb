using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The provider's own delivery outcome for a message, stored verbatim as the
/// provider reports it (Twilio message <c>status</c> values). Kept as strings so
/// the record faithfully mirrors what the provider owns rather than a lossy local
/// projection. Helpers classify the well-known values.
/// </summary>
public static class MessageDeliveryStatus
{
    public const string Accepted = "accepted";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>True when the provider reports the message did not (and will not) reach the handset.</summary>
    public static bool IsFailure(string? status) =>
        string.Equals(status, Undelivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the provider confirms the message reached the handset.</summary>
    public static bool IsDelivered(string? status) =>
        string.Equals(status, Delivered, StringComparison.OrdinalIgnoreCase);

    public static bool IsScheduled(string? status) =>
        string.Equals(status, Scheduled, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the provider's outcome will not change further, so there is no point
    /// re-fetching it: delivered, undelivered, failed or canceled.
    /// </summary>
    public static bool IsTerminal(string? status) =>
        string.Equals(status, Delivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Undelivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Canceled, StringComparison.OrdinalIgnoreCase);
}
