using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major currency units (e.g. dollars).</summary>
    public decimal Price { get; set; }

    /// <summary>Human-friendly price, e.g. "$299.00 / month".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
