using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's Maxio subscription as surfaced by the PublicApi.
/// </summary>
public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price in whole currency units (e.g. dollars).</summary>
    public decimal? Price { get; set; }

    /// <summary>Current Maxio subscription state (wire value, e.g. "active").</summary>
    public string State { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStart { get; set; }

    /// <summary>When the next regularly scheduled billing event occurs.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
