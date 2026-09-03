using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, string shopperId, int contactNumberId,
        NotificationKind kind, string content, DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null, string? idempotencyKey = null)
    {
        OrderId = orderId;
        ShopperId = shopperId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Content = content;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string ShopperId { get; private set; } = null!;
    public int? ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string DeliveryStatus { get; private set; } = "pending-provider";
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderState(string sid, string status, int? errorCode)
    {
        ProviderMessageSid = sid;
        DeliveryStatus = status;
        ProviderErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RecordProviderFailure(string status)
    {
        DeliveryStatus = status;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void DisposeContent()
    {
        Content = null;
        ContentDisposedAt = DateTimeOffset.UtcNow;
    }
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled
}
