using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionMappers
{
    public static SubscriptionPlanDto ToDto(this MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Name = product.Name,
            Handle = product.Handle,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = product.PriceInCents / 100m,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit
        };
    }

    public static SubscriptionDto ToDto(this MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanName = subscription.Product.Name,
            PlanHandle = subscription.Product.Handle,
            PriceInCents = subscription.ProductPriceInCents,
            Price = subscription.ProductPriceInCents / 100m,
            State = subscription.State,
            ActivatedAt = subscription.ActivatedAt,
            NextBillingDate = subscription.NextAssessmentAt,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? string.Empty
        };
    }
}
