using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class NotificationResend : BaseEntity, IAggregateRoot
{
    private NotificationResend() { }

    public NotificationResend(int sourceNotificationId, string idempotencyKey, DateTimeOffset createdAt)
    {
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey);
        CreatedAt = createdAt;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int? ResultNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete(int resultNotificationId) => ResultNotificationId = resultNotificationId;
}
