using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public class NotificationResendRequest : BaseEntity, IAggregateRoot
{
    private NotificationResendRequest() { }

    public NotificationResendRequest(int sourceNotificationId, string idempotencyKey,
        int notificationId, DateTimeOffset createdAt)
    {
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        NotificationId = notificationId;
        CreatedAt = createdAt;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int NotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
