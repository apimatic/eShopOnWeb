using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToPlanDto(this MaxioProduct product) =>
        new()
        {
            PlanId = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit
        };

    public static SubscriptionDto ToSubscriptionDto(this MaxioSubscription subscription) =>
        new()
        {
            SubscriptionId = subscription.Id,
            State = subscription.State ?? string.Empty,
            Plan = subscription.Product?.ToPlanDto(),
            BalanceInCents = subscription.BalanceInCents,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt
        };
}
