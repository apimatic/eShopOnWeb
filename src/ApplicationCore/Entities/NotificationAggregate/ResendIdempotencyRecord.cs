using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Records the outcome of a resend request under a caller-supplied idempotency key, so that a
/// repeat of the same request under the same key returns the earlier result without sending a
/// second message. A genuine second attempt under a fresh key is a different record.
/// </summary>
public class ResendIdempotencyRecord : BaseEntity, IAggregateRoot
{
    private ResendIdempotencyRecord() { } // EF

    public ResendIdempotencyRecord(string key, int originNotificationId, int resultNotificationId)
    {
        Key = Guard.Against.NullOrEmpty(key, nameof(key));
        OriginNotificationId = originNotificationId;
        ResultNotificationId = resultNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string Key { get; private set; } = default!;

    /// <summary>The notification that was re-sent.</summary>
    public int OriginNotificationId { get; private set; }

    /// <summary>The notification produced by the resend (what the endpoint returns).</summary>
    public int ResultNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
