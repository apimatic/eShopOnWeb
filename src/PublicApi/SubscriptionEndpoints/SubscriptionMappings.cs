using System;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToPlanDto(this MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description ?? string.Empty,
        PriceInCents = product.PriceInCents,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    public static SubscriptionDto ToSubscriptionDto(this MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle ?? string.Empty,
        ProductName = subscription.ProductName ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Price = subscription.ProductPriceInCents / 100m,
        Interval = subscription.ProductInterval,
        IntervalUnit = subscription.ProductIntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        CreatedAt = subscription.CreatedAt
    };
}
