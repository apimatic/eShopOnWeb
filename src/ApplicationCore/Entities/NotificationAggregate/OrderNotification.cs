using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        NotificationKind kind,
        string body,
        DateTimeOffset createdAt,
        bool isScheduled = false,
        DateTimeOffset? scheduledFor = null,
        int? sourceNotificationId = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        IsScheduled = isScheduled;
        ScheduledFor = scheduledFor;
        SourceNotificationId = sourceNotificationId;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; } = null!;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ProviderDateUpdated { get; private set; }
    public DateTimeOffset? LastProviderCheckAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }

    public void ApplyProviderState(
        string providerSid,
        string status,
        int? errorCode,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateSent,
        DateTimeOffset? dateUpdated,
        DateTimeOffset checkedAt)
    {
        ProviderSid = providerSid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
        ProviderDateUpdated = dateUpdated;
        LastProviderCheckAt = checkedAt;
    }

    public void MarkProviderFailure(DateTimeOffset checkedAt)
    {
        ProviderStatus = "provider_error";
        LastProviderCheckAt = checkedAt;
    }

    public void MarkContentDisposed(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt = disposedAt;
    }
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled,
    Resend
}
