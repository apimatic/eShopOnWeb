using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A claim on a caller-supplied resend idempotency key. <see cref="Key"/> is the primary key, so
/// a second insert under the same key is rejected by the store (a SQL Server primary-key
/// constraint; the EF in-memory provider likewise rejects a duplicate primary key at SaveChanges)
/// rather than by a check-then-act read. Catching that rejection is how a repeated resend replays
/// its first result instead of sending a second message.
/// </summary>
public class SmsIdempotencyKey : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SmsIdempotencyKey() { }

    public SmsIdempotencyKey(string key, int notificationId)
    {
        Guard.Against.NullOrEmpty(key, nameof(key));

        Key = key;
        NotificationId = notificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The caller-supplied idempotency key — the primary key of this claim.</summary>
    public string Key { get; private set; }

    /// <summary>The notification the first request under this key produced.</summary>
    public int NotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
