using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId,
        NotificationKind kind, string body, DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null, int? originalNotificationId = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = null!;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public bool CancellationPending { get; private set; }

    public void RecordProviderState(string sid, string status, int? errorCode,
        DateTimeOffset? dateCreated, DateTimeOffset? dateSent)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
        if (status == "canceled") CancellationPending = false;
    }

    public void RecordProviderFailure(int? errorCode)
    {
        ProviderStatus = "provider-error";
        ProviderErrorCode = errorCode;
    }

    public void RequestCancellation()
    {
        if (ProviderStatus == "scheduled") CancellationPending = true;
    }

    public void RecordCancelled()
    {
        ProviderStatus = "canceled";
        CancellationPending = false;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt ??= disposedAt;
    }
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled
}
