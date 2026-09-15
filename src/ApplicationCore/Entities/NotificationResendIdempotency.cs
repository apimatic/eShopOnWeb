using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class NotificationResendIdempotency : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private NotificationResendIdempotency() { }

    public NotificationResendIdempotency(string idempotencyKey, int sourceNotificationId, int resultNotificationId)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        Guard.Against.NegativeOrZero(resultNotificationId, nameof(resultNotificationId));

        IdempotencyKey = idempotencyKey;
        SourceNotificationId = sourceNotificationId;
        ResultNotificationId = resultNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; }
    public int SourceNotificationId { get; private set; }
    public int ResultNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
