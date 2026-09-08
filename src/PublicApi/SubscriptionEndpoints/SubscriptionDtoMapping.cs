using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps Maxio contract objects to the PublicApi subscription DTOs.</summary>
internal static class SubscriptionDtoMapping
{
    public static SubscriptionPlanDto ToPlanDto(MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Name = product.Name ?? string.Empty,
            Handle = product.Handle ?? string.Empty,
            Description = product.Description,
            Price = product.PriceInCents / 100m,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            RequiresCreditCard = product.RequireCreditCard,
            Taxable = product.Taxable
        };
    }

    public static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        MaxioProduct? plan = subscription.Product;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            Currency = subscription.Currency ?? string.Empty,
            PlanId = plan?.Id ?? 0,
            PlanHandle = plan?.Handle ?? string.Empty,
            PlanName = plan?.Name ?? string.Empty,
            PlanPrice = (plan?.PriceInCents ?? 0) / 100m,
            Interval = plan?.Interval ?? 1,
            IntervalUnit = plan?.IntervalUnit ?? string.Empty,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            NextBillingDate = subscription.NextBillingAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    public static List<SubscriptionDto> ToSubscriptionDtos(IEnumerable<MaxioSubscription> subscriptions)
    {
        return subscriptions.Select(ToSubscriptionDto).ToList();
    }
}
