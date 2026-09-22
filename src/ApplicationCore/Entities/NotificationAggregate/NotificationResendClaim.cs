using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Idempotency record for an operator resend. The caller-supplied <see cref="IdempotencyKey"/> is
/// the entity's <b>primary key</b>: inserting it first and letting the store reject a duplicate is
/// what stops a repeated request from sending a second message. Primary-key uniqueness is enforced
/// both by SQL Server and by the EF Core in-memory provider's keyed store, so the claim holds in
/// both. A genuine second attempt supplies a fresh key and is admitted normally.
/// </summary>
public class NotificationResendClaim : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private NotificationResendClaim() { }

    public NotificationResendClaim(string idempotencyKey, int sourceNotificationId)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        IdempotencyKey = idempotencyKey;
        SourceNotificationId = sourceNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Primary key — the caller-supplied idempotency key.</summary>
    public string IdempotencyKey { get; private set; }

    public int SourceNotificationId { get; private set; }

    /// <summary>The notification the resend produced, once completed.</summary>
    public int? ResultNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete(int resultNotificationId) => ResultNotificationId = resultNotificationId;
}
