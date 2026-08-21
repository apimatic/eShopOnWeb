using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Local record of an SMS sent (or attempted) for an order, including the provider SID
/// and the last known delivery outcome so later requests can act on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        int? contactNumberId,
        string destinationPhoneNumber,
        OrderNotificationKind kind,
        string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationPhoneNumber, nameof(destinationPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationPhoneNumber = destinationPhoneNumber;
        Kind = kind;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationPhoneNumber { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }

    public bool IsScheduled =>
        string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);

    public bool IsTerminalStatus =>
        string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "cancelled", StringComparison.OrdinalIgnoreCase);

    public bool DidNotReachShopper =>
        string.IsNullOrEmpty(ProviderMessageSid)
        || string.Equals(ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "cancelled", StringComparison.OrdinalIgnoreCase);

    public void RecordProviderAcceptance(string sid, string status, DateTimeOffset? sendAt)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ScheduledSendAt = sendAt;
        LastSyncedAt = DateTimeOffset.UtcNow;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
    }

    public void RecordProviderFailure(string? status, int? errorCode, string? errorMessage)
    {
        ProviderStatus = string.IsNullOrEmpty(status) ? "failed" : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void SyncFromProvider(string status, int? errorCode, string? errorMessage, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (ContentRedacted)
        {
            Body = null;
        }
        else if (body != null)
        {
            Body = body;
        }
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }

    public void MarkAsResendOf(int sourceNotificationId)
    {
        SourceNotificationId = sourceNotificationId;
        Kind = OrderNotificationKind.Resend;
    }
}
