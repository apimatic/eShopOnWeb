using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionClaimStatus
{
    Claimed = 0,
    Completed = 1
}

/// <summary>
/// Claim taken before a subscribe call reaches the billing provider. The composite key
/// (UserName, PlanHandle) makes the store refuse a second concurrent subscribe for the same plan.
/// </summary>
public class SubscriptionClaim
{
    public string UserName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public SubscriptionClaimStatus Status { get; set; } = SubscriptionClaimStatus.Claimed;
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
