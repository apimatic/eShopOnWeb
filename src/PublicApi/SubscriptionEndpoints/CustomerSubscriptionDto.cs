using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a shopper's subscription, as confirmed by Maxio.</summary>
public class CustomerSubscriptionDto
{
    public string Id { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
