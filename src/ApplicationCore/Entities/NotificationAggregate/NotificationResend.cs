using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResend : BaseEntity
{
    private NotificationResend() { }

    public NotificationResend(string idempotencyKey, int originalNotificationId, DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        OriginalNotificationId = originalNotificationId;
        CreatedAt = createdAt;
    }

    public string IdempotencyKey { get; private set; } = null!;
    public int OriginalNotificationId { get; private set; }
    public int? NewNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete(int notificationId) => NewNotificationId = notificationId;
}
