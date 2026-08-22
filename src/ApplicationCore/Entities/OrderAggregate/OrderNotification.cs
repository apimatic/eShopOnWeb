using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        int? contactNumberId,
        string destinationNumber,
        string? body,
        string? providerMessageSid,
        string providerStatus,
        int? providerErrorCode,
        DateTimeOffset? scheduledSendAt,
        int? resentFromNotificationId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ScheduledSendAt = scheduledSendAt;
        ResentFromNotificationId = resentFromNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool HasReachedShopper =>
        string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase);

    public bool IsInFlight =>
        string.Equals(ProviderStatus, "queued", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "sending", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "sent", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "receiving", StringComparison.OrdinalIgnoreCase);

    public bool IsScheduled =>
        string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);

    public bool IsTerminalStatus =>
        HasReachedShopper
        || string.Equals(ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase);

    public void ApplyProviderState(string status, int? errorCode, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        if (!ContentRedacted && body != null)
        {
            Body = body;
        }
    }

    public void RecordProviderSid(string sid)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
