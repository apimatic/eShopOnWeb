using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class NotificationResendClaim : BaseEntity
{
    private NotificationResendClaim() { }

    public NotificationResendClaim(int sourceNotificationId, string idempotencyKey, DateTimeOffset createdAt)
    {
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int? ProducedNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetProducedNotification(int notificationId) => ProducedNotificationId = notificationId;
}
