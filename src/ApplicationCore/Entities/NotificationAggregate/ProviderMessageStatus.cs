using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The provider's own delivery-status wire values, kept here as plain strings so the domain never
/// depends on the messaging SDK. The provider implementation maps its SDK enum onto these.
/// </summary>
public static class ProviderMessageStatus
{
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Accepted = "accepted";
    public const string Receiving = "receiving";
    public const string Received = "received";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string PartiallyDelivered = "partially_delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Scheduled = "scheduled";
    public const string Canceled = "canceled";

    /// <summary>The message was accepted by the carrier and confirmed reaching the handset.</summary>
    public static bool IsDelivered(string? status) =>
        string.Equals(status, Delivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Received, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Read, StringComparison.OrdinalIgnoreCase);

    /// <summary>The provider accepted the message but the carrier ultimately refused/could not deliver it.</summary>
    public static bool IsUndeliverable(string? status) =>
        string.Equals(status, Undelivered, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase);

    /// <summary>Still moving through the provider/carrier — no final outcome yet.</summary>
    public static bool IsInFlight(string? status) =>
        string.Equals(status, Queued, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Sending, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Sent, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Accepted, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Receiving, StringComparison.OrdinalIgnoreCase);
}
