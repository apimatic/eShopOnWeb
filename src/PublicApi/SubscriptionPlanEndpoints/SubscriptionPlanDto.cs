using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresCreditCard { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresCreditCard = plan.RequiresCreditCard
    };
}
