using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public string PriceDisplay { get; set; } = string.Empty;
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
}

public class SubscriptionComponentDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Kind { get; set; }
    public string? UnitName { get; set; }
    public long? PricePerUnitInCents { get; set; }
}

public class SubscriptionDto
{
    public long Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public string? CollectionMethod { get; set; }
}