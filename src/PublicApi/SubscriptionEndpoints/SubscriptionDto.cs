using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription in Maxio Advanced Billing, as reflected back to the shopper.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }

    /// <summary>
    /// Maxio subscription state (e.g. "active").
    /// </summary>
    public string? State { get; set; }

    public string? Reference { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>
    /// Recurring price in the plan's currency.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Recurring price in cents.
    /// </summary>
    public long PriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>
    /// Next billing date.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodEndsOn { get; set; }
}
