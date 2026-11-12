using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A user's subscription to a plan. The composite primary key (UserId, PlanHandle) is the claim:
/// the store refuses a second concurrent claim, so a double-click never creates two subscriptions.
/// </summary>
public class MaxioSubscriptionClaim
{
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    /// <summary>The subscription reference sent to Maxio; also the key used to reconcile an unknown outcome.</summary>
    public string Reference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public ClaimStatus Status { get; set; }
    public DateTime ClaimedAtUtc { get; set; }
}
