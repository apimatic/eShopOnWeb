using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Application-owned idempotency claim for a Maxio subscription enrollment.
/// Maxio remains the billing system of record; this row only serializes writes.
/// </summary>
public sealed class MaxioSubscriptionClaim
{
    private MaxioSubscriptionClaim()
    {
    }

    public long Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public MaxioSubscriptionClaimStatus Status { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public string LeaseToken { get; private set; } = string.Empty;
    public DateTimeOffset LeaseExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static MaxioSubscriptionClaim Create(
        string userId,
        string productHandle,
        string subscriptionReference,
        string leaseToken,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        return new MaxioSubscriptionClaim
        {
            UserId = userId,
            ProductHandle = productHandle,
            SubscriptionReference = subscriptionReference,
            Status = MaxioSubscriptionClaimStatus.Pending,
            LeaseToken = leaseToken,
            LeaseExpiresAt = now.Add(leaseDuration),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void RenewLease(string leaseToken, DateTimeOffset now, TimeSpan leaseDuration)
    {
        LeaseToken = leaseToken;
        LeaseExpiresAt = now.Add(leaseDuration);
        Status = MaxioSubscriptionClaimStatus.Pending;
        UpdatedAt = now;
    }

    public void MarkActive(int? maxioSubscriptionId, DateTimeOffset now)
    {
        Status = MaxioSubscriptionClaimStatus.Active;
        MaxioSubscriptionId = maxioSubscriptionId;
        LeaseExpiresAt = now;
        UpdatedAt = now;
    }

    public void MarkReconciliationRequired(DateTimeOffset now, TimeSpan reconciliationWindow)
    {
        Status = MaxioSubscriptionClaimStatus.ReconciliationRequired;
        LeaseExpiresAt = now.Add(reconciliationWindow);
        UpdatedAt = now;
    }
}

public enum MaxioSubscriptionClaimStatus
{
    Pending,
    Active,
    ReconciliationRequired
}
