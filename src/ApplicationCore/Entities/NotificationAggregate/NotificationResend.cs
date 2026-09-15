using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResend : BaseEntity, IAggregateRoot
{
    private NotificationResend() { }

    public NotificationResend(int notificationId, string idempotencyKey, DateTimeOffset createdAt)
    {
        NotificationId = notificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }

    public int NotificationId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int? ResultNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete(int resultNotificationId) => ResultNotificationId = resultNotificationId;
}
