using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan as exposed over the API.</summary>
public class PlanDto
{
    public int Id { get; set; }

    /// <summary>The durable identifier used to subscribe to this plan.</summary>
    public string Handle { get; set; }

    public string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>The recurring price as a currency amount.</summary>
    public decimal Price { get; set; }

    /// <summary>The recurring price in minor units (cents), free of rounding.</summary>
    public long PriceInCents { get; set; }

    /// <summary>How often the plan bills, e.g. <c>month</c>.</summary>
    public string BillingCadence { get; set; }

    public static PlanDto FromPlan(BillingPlan plan)
    {
        return new PlanDto
        {
            Id = plan.Id,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            PriceInCents = plan.PriceInCents,
            BillingCadence = plan.BillingCadence
        };
    }
}
