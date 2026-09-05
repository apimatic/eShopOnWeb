using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string? BillingInterval { get; set; }

    public static PlanDto FromServiceDto(SubscriptionPlanDto plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.PriceInCents.HasValue ? plan.PriceInCents.Value / 100m : null,
        BillingInterval = plan.Interval is int interval && plan.IntervalUnit is not null
            ? $"every {interval} {plan.IntervalUnit}{(interval == 1 ? string.Empty : "s")}"
            : plan.IntervalUnit
    };
}
