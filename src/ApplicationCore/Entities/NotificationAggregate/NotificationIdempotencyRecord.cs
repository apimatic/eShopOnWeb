using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The durable claim that a resend under a given caller-supplied idempotency key has already been made.
/// A unique constraint on <see cref="IdempotencyKey"/> rejects a second row for the same key, so a repeated
/// request returns the first result instead of sending a second message.
/// </summary>
public class NotificationIdempotencyRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private NotificationIdempotencyRecord() { }
#pragma warning restore CS8618

    public NotificationIdempotencyRecord(string idempotencyKey, int sourceNotificationId, int resultNotificationId)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        IdempotencyKey = idempotencyKey;
        SourceNotificationId = sourceNotificationId;
        ResultNotificationId = resultNotificationId;
    }

    /// <summary>The caller-supplied key. Carries a unique index.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The notification that was asked to be re-sent.</summary>
    public int SourceNotificationId { get; private set; }

    /// <summary>The notification the resend produced.</summary>
    public int ResultNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
