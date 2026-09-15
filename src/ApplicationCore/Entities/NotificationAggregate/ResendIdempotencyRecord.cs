using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Remembers the outcome of an operator resend request keyed by the caller-supplied idempotency key,
/// so that repeating a request under the same key returns the message the first request produced rather
/// than sending a second one. A genuine second attempt uses a fresh key and is not matched here.
/// </summary>
public class ResendIdempotencyRecord : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ResendIdempotencyRecord() { }

    public ResendIdempotencyRecord(string idempotencyKey, int sourceNotificationId, int resultNotificationId)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        IdempotencyKey = idempotencyKey;
        SourceNotificationId = sourceNotificationId;
        ResultNotificationId = resultNotificationId;
    }

    /// <summary>The caller-supplied idempotency key.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The notification the resend was requested for.</summary>
    public int SourceNotificationId { get; private set; }

    /// <summary>The notification the resend produced.</summary>
    public int ResultNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
}
