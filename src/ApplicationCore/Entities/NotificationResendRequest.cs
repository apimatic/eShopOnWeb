using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class NotificationResendRequest : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private NotificationResendRequest() { }
#pragma warning restore CS8618

    public NotificationResendRequest(int sourceNotificationId, string idempotencyKey, int notificationId)
    {
        SourceNotificationId = Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        IdempotencyKey = Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));
        NotificationId = Guard.Against.NegativeOrZero(notificationId, nameof(notificationId));
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int NotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
