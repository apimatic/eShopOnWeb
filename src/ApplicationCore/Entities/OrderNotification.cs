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
        string destination,
        NotificationKind kind,
        string body,
        DateTimeOffset createdAt,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        ContactNumberId = contactNumberId;
        Destination = Guard.Against.NullOrWhiteSpace(destination, nameof(destination));
        Kind = kind;
        Body = Guard.Against.NullOrWhiteSpace(body, nameof(body));
        ProviderStatus = NotificationDeliveryStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public string Destination { get; private set; } = string.Empty;
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = NotificationDeliveryStatus.Pending;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ContentDeletedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderResult(string sid, string status, int? errorCode, DateTimeOffset? scheduledFor, DateTimeOffset updatedAt)
    {
        ProviderMessageSid = Guard.Against.NullOrWhiteSpace(sid, nameof(sid));
        ProviderStatus = Guard.Against.NullOrWhiteSpace(status, nameof(status));
        ProviderErrorCode = errorCode;
        ScheduledFor = scheduledFor;
        UpdatedAt = updatedAt;
    }

    public void RecordProviderFailure(int? errorCode, DateTimeOffset updatedAt)
    {
        ProviderStatus = NotificationDeliveryStatus.ProviderRejected;
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
    }

    public void RefreshProviderState(string status, int? errorCode, bool contentWasRedacted, DateTimeOffset updatedAt)
    {
        ProviderStatus = Guard.Against.NullOrWhiteSpace(status, nameof(status));
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
        if (contentWasRedacted)
        {
            Body = null;
            ContentDeletedAt ??= updatedAt;
        }
    }

    public void MarkContentDeleted(DateTimeOffset deletedAt)
    {
        Body = null;
        ContentDeletedAt = deletedAt;
        UpdatedAt = deletedAt;
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

public static class NotificationDeliveryStatus
{
    public const string Pending = "pending";
    public const string ProviderRejected = "provider_rejected";
}
