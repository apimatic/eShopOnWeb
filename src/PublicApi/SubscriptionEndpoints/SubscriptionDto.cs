using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price => PriceInCents / 100m;
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
