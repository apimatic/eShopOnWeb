using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A row whose primary key is the claim: inserting a second row with the same key is refused by the store,
/// which stops a second caller before it reaches the billing provider.
/// </summary>
public abstract class BillingClaim
{
    public BillingClaimStatus Status { get; protected set; }

    /// <summary>
    /// Concurrency token. Changed only when a stale claim is taken over, so two callers taking over
    /// the same claim cannot both succeed.
    /// </summary>
    public Guid ClaimToken { get; protected set; }

    public DateTimeOffset ClaimedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }

    protected void StartClaim(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Pending;
        ClaimToken = Guid.NewGuid();
        ClaimedAt = now;
        UpdatedAt = now;
    }

    public bool IsSettled => Status == BillingClaimStatus.Completed;

    /// <summary>
    /// An unsettled claim older than <paramref name="staleAfter"/> belongs to a request that has already
    /// exceeded its own deadline, so another request may take it over.
    /// </summary>
    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter) => !IsSettled && now - ClaimedAt >= staleAfter;

    public void TakeOver(DateTimeOffset now) => StartClaim(now);

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Unknown;
        UpdatedAt = now;
    }
}
