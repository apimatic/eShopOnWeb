using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResendAttempt : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private NotificationResendAttempt() { }
#pragma warning restore CS8618

    public NotificationResendAttempt(int originalNotificationId, string idempotencyKey, int resultingNotificationId)
    {
        Guard.Against.OutOfRange(originalNotificationId, nameof(originalNotificationId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.OutOfRange(resultingNotificationId, nameof(resultingNotificationId), 1, int.MaxValue);

        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        ResultingNotificationId = resultingNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OriginalNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int ResultingNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
