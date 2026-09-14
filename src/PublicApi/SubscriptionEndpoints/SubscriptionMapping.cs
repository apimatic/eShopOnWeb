using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps Maxio entities (as returned by the Maxio OpenAPI specification) to API DTOs.</summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToPlanDto(MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = CentsToPrice(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            ProductFamilyName = product.ProductFamily?.Name,
            RequiresCreditCard = product.RequireCreditCard ?? product.RequestCreditCard ?? false,
        };
    }

    public static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            State = subscription.State,
            PlanId = subscription.Product?.Id,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            Price = CentsToPrice(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            Interval = subscription.Product?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit,
            CreatedAt = subscription.CreatedAt,
            ActivatedAt = subscription.ActivatedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        };
    }

    private static decimal CentsToPrice(long? cents)
    {
        return cents.HasValue ? Math.Round(cents.Value / 100m, 2) : 0m;
    }
}
