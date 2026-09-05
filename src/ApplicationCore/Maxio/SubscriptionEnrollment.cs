using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A buyer's enrollment in a Maxio subscription, projected for eShopOnWeb consumers.
/// </summary>
public class SubscriptionEnrollment
{
    public long SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = default!;
    public string PlanName { get; set; } = default!;
    public decimal Price { get; set; }
    public string State { get; set; } = default!;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// True when this enrollment already existed for the buyer/plan (an idempotent replay of Subscribe).
    /// </summary>
    public bool AlreadyExisted { get; set; }
}
