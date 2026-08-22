using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class ResendIdempotencyRecord : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private ResendIdempotencyRecord() { }
    #pragma warning restore CS8618

    public ResendIdempotencyRecord(int notificationId, string idempotencyKey, int resultNotificationId)
    {
        Guard.Against.NegativeOrZero(notificationId, nameof(notificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(resultNotificationId, nameof(resultNotificationId));

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
