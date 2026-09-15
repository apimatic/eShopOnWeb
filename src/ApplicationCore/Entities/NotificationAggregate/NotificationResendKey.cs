using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResendKey : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private NotificationResendKey() {}

    public NotificationResendKey(int notificationId, string idempotencyKey, int resultNotificationId)
    {
        Guard.Against.OutOfRange(notificationId, nameof(notificationId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.OutOfRange(resultNotificationId, nameof(resultNotificationId), 1, int.MaxValue);

        NotificationId = notificationId;
        IdempotencyKey = idempotencyKey;
        ResultNotificationId = resultNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int NotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int ResultNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
