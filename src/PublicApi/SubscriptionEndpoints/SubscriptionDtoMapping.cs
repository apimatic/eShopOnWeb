using Microsoft.eShopWeb.PublicApi.SubscriptionServices;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps subscription domain records to the public API DTOs.</summary>
internal static class SubscriptionDtoMapping
{
    public static SubscriptionPlanDto ToPlanDto(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto
        {
            ProductId = plan.ProductId,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Price = plan.Price,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            RequiresCreditCard = plan.RequiresCreditCard
        };
    }

    public static SubscriptionDto ToDto(SubscriptionRecord subscription)
    {
        return new SubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            State = subscription.State,
            Currency = subscription.Currency,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.Price,
            ProductId = subscription.ProductId,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            NextBillingAt = subscription.NextBillingAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt
        };
    }
}
