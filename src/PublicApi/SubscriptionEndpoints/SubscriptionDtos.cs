using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = "";
    public string PlanName { get; set; } = "";
    public decimal? Price { get; set; }
    public string State { get; set; } = "";
    public DateTimeOffset? NextBillingDate { get; set; }

    public static SubscriptionDto From(SubscriptionInfo s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.Price,
        State = s.State,
        NextBillingDate = s.NextBillingDate
    };
}
