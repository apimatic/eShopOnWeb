using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API projection of a subscribable plan.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major currency units (e.g. dollars).</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>Billing period unit (e.g. <c>month</c>).</summary>
    public string Interval { get; set; } = "month";

    public int IntervalCount { get; set; } = 1;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Human-friendly price, e.g. <c>$299.00/month</c>.</summary>
    public string DisplayPrice { get; set; } = string.Empty;

    public static SubscriptionPlanDto FromDomain(SubscriptionPlan plan)
    {
        var price = plan.PriceInCents / 100m;
        return new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Price = price,
            Currency = plan.Currency,
            Interval = plan.Interval,
            IntervalCount = plan.IntervalCount,
            ProductFamilyHandle = plan.ProductFamilyHandle,
            DisplayPrice = SubscriptionDisplay.FormatPrice(price, plan.Currency, plan.Interval, plan.IntervalCount)
        };
    }
}
