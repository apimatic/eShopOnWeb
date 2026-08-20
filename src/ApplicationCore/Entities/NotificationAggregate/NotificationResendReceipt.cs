using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Records a caller-supplied idempotency key for an operator resend so the same
/// key cannot produce a second Twilio message.
/// </summary>
public class NotificationResendReceipt : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private NotificationResendReceipt() { }
    #pragma warning restore CS8618

    public NotificationResendReceipt(int originalNotificationId, string idempotencyKey, int resultNotificationId)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(resultNotificationId, nameof(resultNotificationId));

        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        ResultNotificationId = resultNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OriginalNotificationId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public int ResultNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
