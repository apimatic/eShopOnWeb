using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public sealed class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, int contactNumberId, NotificationKind kind, string content,
        int? sourceNotificationId = null, string? idempotencyKey = null)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId);
        ContactNumberId = Guard.Against.NegativeOrZero(contactNumberId);
        Kind = kind;
        Content = Guard.Against.NullOrWhiteSpace(content);
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        DeliveryStatus = NotificationDeliveryStatus.Pending;
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public bool ContentDisposed { get; private set; }
    public NotificationDeliveryStatus DeliveryStatus { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }

    public void RecordProviderMessage(ProviderMessage message, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = message.Sid;
        ScheduledFor = scheduledFor;
        ApplyProviderState(message);
    }

    public void ApplyProviderState(ProviderMessage message)
    {
        ProviderStatus = message.Status;
        ProviderErrorCode = message.ErrorCode;
        ProviderErrorMessage = message.ErrorMessage;
        ProviderCreatedAt = message.DateCreated ?? ProviderCreatedAt;
        ProviderSentAt = message.DateSent ?? ProviderSentAt;
        LastCheckedAt = DateTimeOffset.UtcNow;
        DeliveryStatus = MapDeliveryStatus(message.Status);
    }

    public void RecordProviderFailure(string errorMessage)
    {
        ProviderErrorMessage = errorMessage;
        DeliveryStatus = NotificationDeliveryStatus.ProviderRequestFailed;
        LastCheckedAt = DateTimeOffset.UtcNow;
    }

    public void DisposeContent()
    {
        Content = null;
        ContentDisposed = true;
    }

    private static NotificationDeliveryStatus MapDeliveryStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "delivered" => NotificationDeliveryStatus.Delivered,
        "failed" => NotificationDeliveryStatus.Failed,
        "undelivered" => NotificationDeliveryStatus.Undelivered,
        "canceled" => NotificationDeliveryStatus.Cancelled,
        "scheduled" => NotificationDeliveryStatus.Scheduled,
        "sent" => NotificationDeliveryStatus.Sent,
        "queued" or "accepted" or "sending" or "receiving" or "received" or "read" => NotificationDeliveryStatus.InProgress,
        _ => NotificationDeliveryStatus.Unknown
    };
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled
}

public enum NotificationDeliveryStatus
{
    Pending,
    InProgress,
    Scheduled,
    Sent,
    Delivered,
    Failed,
    Undelivered,
    Cancelled,
    ProviderRequestFailed,
    Unknown
}
