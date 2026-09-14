using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps Maxio contracts to the PublicApi response DTOs.</summary>
public static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToPlanDto(MaxioProduct product, string currency)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = ToAmount(product.PriceInCents),
            Currency = currency,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? "month",
            RequiresCreditCard = product.RequireCreditCard
        };
    }

    public static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, string currency)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Price = ToAmount(subscription.ProductPriceInCents),
            Currency = currency,
            BalanceInCents = subscription.BalanceInCents,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? string.Empty,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    public static decimal ToAmount(long cents)
    {
        return Math.Round(cents / 100m, 2);
    }
}
