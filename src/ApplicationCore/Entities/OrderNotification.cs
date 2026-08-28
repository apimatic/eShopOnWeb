using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        NotificationType type,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledAt = null,
        int? sourceNotificationId = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        Type = type;
        Body = body;
        CreatedAt = createdAt;
        ScheduledAt = scheduledAt;
        SourceNotificationId = sourceNotificationId;
        DeliveryStatus = NotificationDeliveryStatuses.PendingProvider;
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationType Type { get; private set; }
    public string? Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string DeliveryStatus { get; private set; } = NotificationDeliveryStatuses.PendingProvider;
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? LastProviderCheckAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public bool ProviderCancellationPending { get; private set; }

    public void ApplyProviderState(SmsProviderMessage message, DateTimeOffset checkedAt)
    {
        ProviderMessageSid = message.Sid;
        DeliveryStatus = message.Status;
        ProviderErrorCode = message.ErrorCode;
        ProviderDateCreated = message.DateCreated;
        ProviderDateSent = message.DateSent;
        LastProviderCheckAt = checkedAt;
        if (message.Status != "scheduled")
        {
            ProviderCancellationPending = false;
        }
    }

    public void MarkProviderFailure(int? errorCode, DateTimeOffset checkedAt)
    {
        DeliveryStatus = NotificationDeliveryStatuses.ProviderRequestFailed;
        ProviderErrorCode = errorCode;
        LastProviderCheckAt = checkedAt;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt ??= disposedAt;
    }

    public void RequestProviderCancellation() => ProviderCancellationPending = true;
}

public enum NotificationType
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled
}

public static class NotificationDeliveryStatuses
{
    public const string PendingProvider = "pending-provider";
    public const string ProviderRequestFailed = "provider-request-failed";
}
