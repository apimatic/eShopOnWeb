using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A local idempotency record for a subscription held by Maxio.
/// Maxio, rather than this record, is the billing system of record.
/// </summary>
public class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userId, string productHandle)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CreatedAt = DateTimeOffset.UtcNow;
        RenewClaim();
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClaimExpiresAt { get; private set; }

    public bool IsClaimExpired(DateTimeOffset now) => ClaimExpiresAt is null || ClaimExpiresAt <= now;

    public void RenewClaim() => ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1);

    public void Complete(int maxioCustomerId, int maxioSubscriptionId)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        ClaimExpiresAt = null;
    }
}
