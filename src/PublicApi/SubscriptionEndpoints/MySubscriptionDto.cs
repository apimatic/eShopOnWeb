using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription as reflected in the buyer's account.</summary>
public class MySubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}
