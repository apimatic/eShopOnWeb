using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription belonging to the authenticated user, as reported by Maxio.</summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }

    /// <summary>The next billing date (when the current period ends and the next charge occurs).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
