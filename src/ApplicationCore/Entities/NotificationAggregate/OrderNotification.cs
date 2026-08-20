using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        string? destinationNumber)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        CreatedAt = DateTimeOffset.UtcNow;
        DeliveryStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? Body { get; private set; }
    public string DeliveryStatus { get; private set; } = "pending";
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? DestinationNumber { get; private set; }
    public string? ProviderFrom { get; private set; }
    public string? ProviderDateCreated { get; private set; }
    public string? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public void RecordProviderAcceptance(
        string sid,
        string status,
        string? from,
        string? dateCreated,
        string? dateSent,
        DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderSid = sid;
        ApplyProviderState(status, errorCode: null, errorMessage: null, from, dateCreated, dateSent);
        ScheduledSendAt = scheduledSendAt;
    }

    public void RecordLocalSendFailure(string reason)
    {
        DeliveryStatus = "failed";
        ErrorMessage = reason;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(
        string status,
        int? errorCode,
        string? errorMessage,
        string? from,
        string? dateCreated,
        string? dateSent)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        DeliveryStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (from is not null)
        {
            ProviderFrom = from;
        }
        if (dateCreated is not null)
        {
            ProviderDateCreated = dateCreated;
        }
        ProviderDateSent = dateSent;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkResentFrom(int originalNotificationId)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        ResentFromNotificationId = originalNotificationId;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool DidNotReachShopper()
    {
        return DeliveryStatus is "failed" or "undelivered" or "canceled" or "pending";
    }
}
