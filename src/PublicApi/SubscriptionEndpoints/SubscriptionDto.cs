using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as surfaced back to the shopper: plan, price, state and
/// next billing date, sourced from Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }

    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public long? PriceInCents { get; set; }

    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    /// <summary>
    /// When the next regularly scheduled charge will occur (Maxio's
    /// current_period_ends_at).
    /// </summary>
    public DateTime? NextBillingAt { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? CanceledAt { get; set; }
}
