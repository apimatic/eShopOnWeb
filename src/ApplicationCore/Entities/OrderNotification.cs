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
        NotificationType type,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? parentNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId);
        ContactNumberId = Guard.Against.NegativeOrZero(contactNumberId);
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId);
        Type = type;
        Body = Guard.Against.NullOrWhiteSpace(body);
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ParentNotificationId = parentNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = NotificationProviderStatuses.Pending;
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public NotificationType Type { get; private set; }
    public string? Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = NotificationProviderStatuses.Pending;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public bool IsContentRedacted { get; private set; }

    public void RecordProviderState(
        string sid,
        string status,
        int? errorCode,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateSent,
        DateTimeOffset syncedAt)
    {
        ProviderMessageSid = Guard.Against.NullOrWhiteSpace(sid);
        ProviderStatus = Guard.Against.NullOrWhiteSpace(status);
        ProviderErrorCode = errorCode;
        ProviderDateCreated = dateCreated ?? ProviderDateCreated;
        ProviderDateSent = dateSent ?? ProviderDateSent;
        LastSyncedAt = syncedAt;
    }

    public void RecordSendFailure(int? errorCode, DateTimeOffset syncedAt)
    {
        ProviderStatus = NotificationProviderStatuses.SendFailed;
        ProviderErrorCode = errorCode;
        LastSyncedAt = syncedAt;
    }

    public void Redact(DateTimeOffset syncedAt)
    {
        Body = null;
        IsContentRedacted = true;
        LastSyncedAt = syncedAt;
    }
}

public enum NotificationType
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCanceled,
    Resend
}

public static class NotificationProviderStatuses
{
    public const string Pending = "pending";
    public const string SendFailed = "send-failed";
}
