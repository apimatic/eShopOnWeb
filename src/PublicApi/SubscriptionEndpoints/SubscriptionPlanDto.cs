using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanDto
{
    public long Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring amount in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring amount, formatted as decimal dollars/cents.</summary>
    public decimal Price { get; set; }

    /// <summary>Billing interval (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (month or day).</summary>
    public string IntervalUnit { get; set; } = "month";

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public static SubscriptionPlanDto From(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Id = plan.Id,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Price = plan.PriceInCents / 100m,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            ProductFamilyHandle = plan.ProductFamilyHandle
        };
    }
}
