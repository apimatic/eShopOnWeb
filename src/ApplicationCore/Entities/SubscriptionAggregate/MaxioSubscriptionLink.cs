using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShopOnWeb user + plan to its billing subscription. The composite primary key
/// (UserKey, PlanHandle) is the claim: a second concurrent subscribe to the same plan is refused by the store.
/// </summary>
public class MaxioSubscriptionLink
{
    public string UserKey { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    /// <summary>Reference sent to the billing system; used to find the subscription again.</summary>
    public string Reference { get; set; } = string.Empty;
    public ClaimStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
