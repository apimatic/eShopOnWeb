using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a shopper's subscription.</summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? State { get; set; }
    public decimal? Price { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public string? Reference { get; set; }
}
