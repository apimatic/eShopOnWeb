using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription on file for the authenticated shopper.
/// </summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>When the next regularly scheduled charge occurs. Null for end-of-life states.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
