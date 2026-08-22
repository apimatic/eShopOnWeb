using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

public class NotificationResendKey : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private NotificationResendKey() { }
#pragma warning restore CS8618

    public NotificationResendKey(int sourceNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int ResultNotificationId { get; private set; }

    public void AssignResult(int notificationId)
    {
        Guard.Against.NegativeOrZero(notificationId, nameof(notificationId));
        ResultNotificationId = notificationId;
    }
}
