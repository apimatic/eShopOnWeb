using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingMappings
{
    public static SubscriptionPlanDto ToDto(this BillingPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Id = plan.Id,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description ?? string.Empty,
            PriceInCents = plan.PriceInCents,
            Price = plan.Price,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            RequiresPaymentMethod = plan.RequiresPaymentMethod
        };
    }

    public static SubscriptionDto ToDto(this BillingSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.Price,
            NextBillingDate = subscription.NextBillingDate
        };
    }
}
