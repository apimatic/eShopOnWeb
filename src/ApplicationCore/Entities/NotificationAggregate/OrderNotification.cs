using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        string buyerId,
        NotificationKind kind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        BuyerId = buyerId;
        Kind = kind;
        Content = content;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = NotificationDeliveryStatus.Pending;
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = null!;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderState(string providerMessageSid, string status, int? errorCode,
        DateTimeOffset? sentAt, DateTimeOffset now)
    {
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        SentAt = sentAt;
        UpdatedAt = now;
    }

    public void RecordProviderFailure(int? errorCode, DateTimeOffset now)
    {
        ProviderStatus = NotificationDeliveryStatus.ProviderError;
        ProviderErrorCode = errorCode;
        UpdatedAt = now;
    }

    public void DisposeContent(DateTimeOffset now)
    {
        Content = null;
        ContentDisposedAt ??= now;
        UpdatedAt = now;
    }
}
