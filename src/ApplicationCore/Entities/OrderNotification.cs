using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId,
        NotificationKind kind, string body, DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null, int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? LastProviderCheckAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void ApplyProviderState(string sid, string status, int? errorCode,
        DateTimeOffset? sentAt, DateTimeOffset checkedAt)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderSentAt = sentAt;
        LastProviderCheckAt = checkedAt;
    }

    public void MarkProviderFailure(int? errorCode, DateTimeOffset checkedAt)
    {
        ProviderStatus = "local-failed";
        ProviderErrorCode = errorCode;
        LastProviderCheckAt = checkedAt;
    }

    public void MarkCancellationFailure(int? errorCode, DateTimeOffset checkedAt)
    {
        ProviderStatus = "cancellation-failed";
        ProviderErrorCode = errorCode;
        LastProviderCheckAt = checkedAt;
    }

    public void Redact(DateTimeOffset checkedAt)
    {
        Body = null;
        ContentRedacted = true;
        LastProviderCheckAt = checkedAt;
    }
}
