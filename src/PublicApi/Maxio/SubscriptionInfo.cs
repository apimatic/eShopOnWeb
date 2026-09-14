using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A subscription owned by a Maxio customer.</summary>
public class SubscriptionInfo
{
    /// <summary>Maxio subscription id. Not stable across sites/seedings — for display only.</summary>
    public int? SubscriptionId { get; set; }

    /// <summary>Handle of the subscribed plan (e.g. <c>eshop-pro</c>).</summary>
    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>The recurring price actually billed, in dollars.</summary>
    public decimal? PriceAmount { get; set; }

    /// <summary>Raw Maxio subscription state (e.g. <c>active</c>, <c>unpaid</c>, ...).</summary>
    public string? State { get; set; }

    /// <summary>Next billing date, when one is scheduled.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>End of the current billing period, when known.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
