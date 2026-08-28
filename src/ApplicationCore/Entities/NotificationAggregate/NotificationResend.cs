using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed class NotificationResend : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private NotificationResend() { }
#pragma warning restore CS8618

    public NotificationResend(int sourceNotificationId, string idempotencyKey, DateTimeOffset createdAt)
    {
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int? ResultNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete(int notificationId) => ResultNotificationId = notificationId;
}
