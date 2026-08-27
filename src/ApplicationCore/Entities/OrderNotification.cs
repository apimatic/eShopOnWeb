using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        string buyerId,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Kind = kind;
        Body = Guard.Against.NullOrWhiteSpace(body, nameof(body));
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderState(string sid, string status, int? errorCode)
    {
        ProviderMessageSid = Guard.Against.NullOrWhiteSpace(sid, nameof(sid));
        ProviderStatus = Guard.Against.NullOrWhiteSpace(status, nameof(status));
        ProviderErrorCode = errorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordFailure(int? errorCode)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void DisposeContent()
    {
        Body = null;
        ContentDisposedAt ??= DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
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
