using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Maxio models to the PublicApi subscription DTOs.
/// </summary>
internal static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto FromPlan(MaxioProduct product, string? currency)
    {
        var priceInCents = product.PriceInCents ?? 0;
        return new SubscriptionPlanDto
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            Price = FromCents(priceInCents),
            PriceInCents = priceInCents,
            Currency = currency ?? string.Empty,
            Interval = product.Interval ?? 1,
            IntervalUnit = product.IntervalUnit ?? "month",
            MaxioProductId = product.Id ?? 0,
            Taxable = product.Taxable
        };
    }

    public static SubscriptionDto FromSubscription(MaxioSubscription subscription)
    {
        var priceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id ?? 0,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = priceInCents,
            Price = FromCents(priceInCents),
            Currency = subscription.Currency,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt,
            ActivatedAt = subscription.ActivatedAt,
            BalanceInCents = subscription.BalanceInCents,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
        };
    }

    private static decimal FromCents(long priceInCents)
    {
        return priceInCents / 100m;
    }
}
