using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null,
        string? resendIdempotencyKey = null)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        ContactNumberId = Guard.Against.NegativeOrZero(contactNumberId, nameof(contactNumberId));
        Kind = kind;
        Body = Guard.Against.NullOrWhiteSpace(body, nameof(body));
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public int? ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = NotificationStatuses.Pending;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public bool CancellationRequested { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void ApplyProviderState(SmsMessageSnapshot message)
    {
        ProviderMessageSid = message.Sid;
        ProviderStatus = message.Status;
        ProviderErrorCode = message.ErrorCode;
        ProviderCreatedAt = message.DateCreated;
        ProviderUpdatedAt = message.DateUpdated;
        ProviderSentAt = message.DateSent;
    }

    public void MarkProviderRejected(int? errorCode)
    {
        ProviderStatus = NotificationStatuses.ProviderRejected;
        ProviderErrorCode = errorCode;
        ProviderUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RequestCancellation()
    {
        CancellationRequested = true;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt = disposedAt;
    }
}

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}

public static class NotificationStatuses
{
    public const string Pending = "pending";
    public const string ProviderRejected = "provider-rejected";
}
