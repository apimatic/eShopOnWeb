using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResendRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private NotificationResendRecord() { }
#pragma warning restore CS8618

    public NotificationResendRecord(int originalNotificationId, string idempotencyKey, int resultingNotificationId)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(resultingNotificationId, nameof(resultingNotificationId));

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
