using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The atomic claim that a particular resend idempotency key has been used. The
/// <see cref="IdempotencyKey"/> is this entity's PRIMARY KEY, so inserting a second row for the
/// same key is rejected by the store (SQL Server and the EF in-memory provider both enforce PK
/// uniqueness) — that rejection, caught by the service, is what makes a repeated resend under the
/// same key a no-op while a fresh key remains a legitimate second attempt.
/// </summary>
public class ResendClaim : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ResendClaim() { }

    public ResendClaim(string idempotencyKey, int originalNotificationId)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
        OriginalNotificationId = originalNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied idempotency key. Primary key of the table.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The notification this resend was for.</summary>
    public int OriginalNotificationId { get; private set; }

    /// <summary>The notification the resend produced (set once the send has been recorded).</summary>
    public int? ResultingNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void SetResult(int resultingNotificationId) => ResultingNotificationId = resultingNotificationId;
}
