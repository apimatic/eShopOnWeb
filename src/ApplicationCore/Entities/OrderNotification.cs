using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        string shopperId,
        NotificationKind kind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        ShopperId = shopperId;
        Kind = kind;
        Content = content;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string ShopperId { get; private set; } = null!;
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = null!;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderState(
        string sid,
        string status,
        int? errorCode,
        DateTimeOffset? providerCreatedAt,
        DateTimeOffset? providerSentAt,
        DateTimeOffset updatedAt)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderCreatedAt = providerCreatedAt;
        ProviderSentAt = providerSentAt;
        UpdatedAt = updatedAt;
    }

    public void RecordProviderFailure(int? errorCode, DateTimeOffset updatedAt)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
    }

    public void RequestCancellation(DateTimeOffset requestedAt)
    {
        CancellationRequestedAt ??= requestedAt;
        UpdatedAt = requestedAt;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Content = null;
        ContentDisposedAt = disposedAt;
        UpdatedAt = disposedAt;
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
