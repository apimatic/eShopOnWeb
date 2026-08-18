using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Remembers a caller-supplied idempotency key for a resend, together with the notification that key
/// produced. Repeating a resend under the same key returns the same result and sends nothing more; a
/// genuine second attempt under a fresh key sends again.
/// </summary>
public class ResendIdempotencyRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private ResendIdempotencyRecord() { }
#pragma warning restore CS8618

    public ResendIdempotencyRecord(string idempotencyKey, int sourceNotificationId, int resultNotificationId)
    {
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        SourceNotificationId = sourceNotificationId;
        ResultNotificationId = resultNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; }

    /// <summary>The notification that was asked to be resent.</summary>
    public int SourceNotificationId { get; private set; }

    /// <summary>The new notification the resend produced.</summary>
    public int ResultNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
