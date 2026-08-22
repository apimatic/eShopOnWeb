using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class ResendIdempotencyRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private ResendIdempotencyRecord() { }
#pragma warning restore CS8618

    public ResendIdempotencyRecord(int sourceNotificationId, string idempotencyKey, int resultingNotificationId)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(resultingNotificationId, nameof(resultingNotificationId));

        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        ResultingNotificationId = resultingNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int ResultingNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
