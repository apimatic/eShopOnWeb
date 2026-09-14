using System;

using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public decimal? Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool? RequiresCreditCard { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlanInfo plan)
    {
        return new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Price = plan.Price,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            RequiresCreditCard = plan.RequiresCreditCard
        };
    }
}
