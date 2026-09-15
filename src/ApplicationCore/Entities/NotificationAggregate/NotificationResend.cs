using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResend : BaseEntity
{
    private NotificationResend() { }

    public NotificationResend(int sourceNotificationId, OrderNotification notification, string idempotencyKey)
    {
        SourceNotificationId = sourceNotificationId;
        Notification = notification;
        IdempotencyKey = idempotencyKey;
    }

    public int SourceNotificationId { get; private set; }
    public int NotificationId { get; private set; }
    public OrderNotification Notification { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
