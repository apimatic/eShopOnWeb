using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a subscribable Maxio plan.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto FromDomain(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        ProductId = plan.ProductId,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequireCreditCard
    };
}
