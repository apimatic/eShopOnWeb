using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class BillingPlanDto
{
    public int ProductId { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public int Interval { get; init; }

    public static BillingPlanDto FromDomain(BillingPlan plan) => new()
    {
        ProductId = plan.ProductId,
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        IntervalUnit = plan.IntervalUnit,
        Interval = plan.Interval,
    };
}
