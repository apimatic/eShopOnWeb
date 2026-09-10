using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as reflected in Maxio.
/// </summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }

    /// <summary>Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>The next date the subscription will be billed.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
