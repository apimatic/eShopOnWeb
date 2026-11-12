using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum SubscriptionClaimStatus
{
    Pending = 0,
    Completed = 1
}

/// <summary>
/// One row per eShop user who has started a subscribe. The <see cref="UserId"/> primary key is the
/// duplicate-prevention claim: the store refuses a second insert for the same user, so a double-click
/// never reaches the billing provider twice.
/// </summary>
public class SubscriptionClaim
{
    public string UserId { get; private set; } = string.Empty;
    public SubscriptionClaimStatus Status { get; private set; }
    public string PlanHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private SubscriptionClaim() { }

    public SubscriptionClaim(string userId, string planHandle, string subscriptionReference, DateTime createdAtUtc)
    {
        UserId = userId;
        PlanHandle = planHandle;
        SubscriptionReference = subscriptionReference;
        CreatedAtUtc = createdAtUtc;
        Status = SubscriptionClaimStatus.Pending;
    }

    public void SetCustomer(int maxioCustomerId) => MaxioCustomerId = maxioCustomerId;

    public void Complete(int? maxioSubscriptionId)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        Status = SubscriptionClaimStatus.Completed;
    }
}
