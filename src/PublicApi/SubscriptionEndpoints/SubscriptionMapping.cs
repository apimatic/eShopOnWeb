using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Maxio Advanced Billing resources onto the API DTOs exposed to shoppers.
/// </summary>
internal static class SubscriptionMapping
{
    public static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        var priceInCents = subscription.ProductPriceInCents > 0
            ? subscription.ProductPriceInCents
            : product?.PriceInCents ?? 0;

        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id ?? 0,
            State = subscription.State ?? string.Empty,
            Currency = string.IsNullOrWhiteSpace(subscription.Currency) ? "USD" : subscription.Currency,
            PriceInCents = priceInCents,
            Price = priceInCents / 100m,
            PlanId = product?.Id,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PlanInterval = product?.Interval ?? 1,
            PlanIntervalUnit = product?.IntervalUnit ?? "month",
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
        };
    }
}
