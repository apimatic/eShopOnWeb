using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
{
    // States in which a subscription no longer bills and a shopper may re-subscribe.
    private static readonly HashSet<string> EndOfLifeStates = new()
    {
        "canceled", "expired", "failed_to_create", "trial_ended", "on_hold", "suspended"
    };

    public static bool IsLive(MaxioSubscription subscription) => !EndOfLifeStates.Contains(subscription.State);

    public static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    public static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Price = subscription.ProductPriceInCents / 100m,
        State = subscription.State,
        NextBillingAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };
}
