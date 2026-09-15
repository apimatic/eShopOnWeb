using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class NotificationResendRequest : BaseEntity, IAggregateRoot
{
    private NotificationResendRequest() { }

    public NotificationResendRequest(int originalNotificationId, string idempotencyKeyHash,
        int notificationId, DateTimeOffset createdAt)
    {
        OriginalNotificationId = originalNotificationId;
        IdempotencyKeyHash = idempotencyKeyHash;
        NotificationId = notificationId;
        CreatedAt = createdAt;
    }

    public int OriginalNotificationId { get; private set; }
    public string IdempotencyKeyHash { get; private set; } = null!;
    public int NotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
