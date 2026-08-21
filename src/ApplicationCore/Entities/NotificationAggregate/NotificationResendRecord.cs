using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResendRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private NotificationResendRecord() { }
#pragma warning restore CS8618

    public NotificationResendRecord(string idempotencyKey, int originalNotificationId, int resultingNotificationId)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        Guard.Against.NegativeOrZero(resultingNotificationId, nameof(resultingNotificationId));

        IdempotencyKey = idempotencyKey;
        OriginalNotificationId = originalNotificationId;
        ResultingNotificationId = resultingNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; }
    public int OriginalNotificationId { get; private set; }
    public int ResultingNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
