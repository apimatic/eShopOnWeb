using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A Maxio subscription belonging to an eShopOnWeb customer.
/// </summary>
public class CustomerSubscription
{
    public long MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// True when this call created a brand-new subscription; false when an existing,
    /// already-enrolled subscription for this customer/plan was returned instead.
    /// </summary>
    public bool IsNewlyCreated { get; set; }
}
