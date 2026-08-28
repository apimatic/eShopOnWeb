using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

public sealed class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, int contactNumberId, string buyerId,
        NotificationKind kind, string body, DateTimeOffset createdAt,
        int? originalNotificationId = null, string? idempotencyKey = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ProviderStatus = "pending";
        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentDisposed { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderMessage(string sid, string status, int? errorCode,
        DateTimeOffset updatedAt, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
        ScheduledFor = scheduledFor;
    }

    public void RecordProviderFailure(int? errorCode, DateTimeOffset updatedAt)
    {
        ProviderStatus = "provider-failed";
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
    }

    public void RefreshProviderState(string status, int? errorCode, DateTimeOffset updatedAt)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
    }

    public void DisposeContent(DateTimeOffset updatedAt)
    {
        Body = null;
        ContentDisposed = true;
        UpdatedAt = updatedAt;
    }
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled
}
