using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, int contactNumberId, NotificationKind kind, string content,
        DateTimeOffset createdAt, int? sourceNotificationId = null, string? idempotencyKey = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Content = content;
        CreatedAt = createdAt;
        ProviderStatus = "pending";
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderState(string sid, string status, int? errorCode,
        DateTimeOffset? dateCreated, DateTimeOffset? dateSent, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
        ScheduledFor = scheduledFor;
    }

    public void RecordSendFailure() => ProviderStatus = "send_failed";

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Content = null;
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
