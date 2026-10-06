using Microsoft.eShopWeb.Infrastructure.Services.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Maxio API DTOs to the PublicApi response DTOs.
/// </summary>
public static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToPlanDto(MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle ?? "",
            Name = product.Name ?? "",
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = product.PriceInCents / 100m,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? ""
        };
    }

    public static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? "",
            PlanHandle = subscription.Product?.Handle ?? "",
            PlanName = subscription.Product?.Name ?? "",
            PriceInCents = subscription.ProductPriceInCents,
            Price = subscription.ProductPriceInCents / 100m,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt
        };
    }
}
