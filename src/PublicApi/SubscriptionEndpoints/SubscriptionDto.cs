using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription owned by the authenticated user, as recorded in Maxio.
/// </summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string Price { get; set; } = string.Empty;

    /// <summary>
    /// Maxio subscription state, e.g. "active", "canceled", "past_due".
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Next billing date (falls back to the current period end when Maxio
    /// does not report next_billing_at, e.g. for remittance subscriptions).
    /// </summary>
    public DateTime? NextBillingDate { get; set; }

    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? CreatedAt { get; set; }
}
