using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Claim on "subscribe this user to this plan". The composite primary key (UserKey, PlanHandle) rejects a second claimant.
/// </summary>
public class MaxioSubscriptionClaim
{
    public string UserKey { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public ClaimStatus Status { get; set; } = ClaimStatus.Pending;
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
