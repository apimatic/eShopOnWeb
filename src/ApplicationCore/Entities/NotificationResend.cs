using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class NotificationResend : BaseEntity
{
    private NotificationResend() { }

    public NotificationResend(int sourceNotificationId, string idempotencyKeyHash, int notificationId, DateTimeOffset createdAt)
    {
        SourceNotificationId = sourceNotificationId;
        IdempotencyKeyHash = idempotencyKeyHash;
        NotificationId = notificationId;
        CreatedAt = createdAt;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKeyHash { get; private set; } = string.Empty;
    public int NotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
