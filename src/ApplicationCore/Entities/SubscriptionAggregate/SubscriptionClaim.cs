using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionClaimStatus
{
    /// <summary>Claimed locally; the Maxio writes are in flight.</summary>
    Pending = 0,
    /// <summary>The Maxio subscription exists and its ids are recorded.</summary>
    Active = 1,
    /// <summary>A Maxio write may have landed but could not be confirmed; settled by the next subscribe.</summary>
    Unknown = 2
}

/// <summary>
/// One row per (buyer, plan). The composite primary key is what rejects a second concurrent subscribe
/// before it reaches Maxio.
/// </summary>
public class SubscriptionClaim
{
    public string BuyerId { get; private set; }
    public string PlanHandle { get; private set; }
    public SubscriptionClaimStatus Status { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }

    private SubscriptionClaim() { BuyerId = string.Empty; PlanHandle = string.Empty; } // Required by EF

    public SubscriptionClaim(string buyerId, string planHandle, DateTimeOffset claimedAt)
    {
        BuyerId = buyerId;
        PlanHandle = planHandle;
        ClaimedAt = claimedAt;
        Status = SubscriptionClaimStatus.Pending;
    }

    public void MarkActive(int? customerId, int? subscriptionId)
    {
        Status = SubscriptionClaimStatus.Active;
        MaxioCustomerId = customerId ?? MaxioCustomerId;
        MaxioSubscriptionId = subscriptionId;
    }

    public void MarkUnknown() => Status = SubscriptionClaimStatus.Unknown;
}
