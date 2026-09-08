using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}

public class MySubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
}
