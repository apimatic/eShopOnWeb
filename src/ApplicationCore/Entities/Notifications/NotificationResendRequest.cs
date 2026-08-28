using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public class NotificationResendRequest : BaseEntity, IAggregateRoot
{
    private NotificationResendRequest() { }

    public NotificationResendRequest(int originalNotificationId, string idempotencyKey, int notificationId)
    {
        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        NotificationId = notificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OriginalNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int NotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
