using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string PlanHandle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public static SubscriptionPlanDto From(MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            PlanHandle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            Price = (product.PriceInCents ?? 0) / 100m,
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit ?? string.Empty
        };
    }
}
