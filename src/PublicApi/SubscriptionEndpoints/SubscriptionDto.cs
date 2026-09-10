using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription held by the authenticated shopper, as reported by Maxio.</summary>
public class SubscriptionDto
{
    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; set; }

    /// <summary>The plan (product) handle the subscription is on.</summary>
    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>The Maxio subscription state, e.g. "active".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Current recurring amount in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>End of the current billing period (when the next charge is scheduled).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio will next attempt to bill the subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>The Maxio customer id the subscription belongs to.</summary>
    public int CustomerId { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
