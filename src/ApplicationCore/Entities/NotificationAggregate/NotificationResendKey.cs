using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Application-level idempotency for operator resends. Twilio's Message create
/// has no idempotency key, so the shop stores the caller key before a second send is possible.
/// </summary>
public class NotificationResendKey : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private NotificationResendKey() { }
    #pragma warning restore CS8618

    public NotificationResendKey(int sourceNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int SourceNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int? ResultNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void AssignResult(int resultNotificationId)
    {
        Guard.Against.NegativeOrZero(resultNotificationId, nameof(resultNotificationId));
        ResultNotificationId = resultNotificationId;
    }
}
