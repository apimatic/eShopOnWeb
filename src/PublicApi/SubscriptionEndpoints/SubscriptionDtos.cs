using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new(
        plan.Handle,
        plan.Name,
        plan.Description,
        plan.PriceInCents,
        plan.Interval,
        plan.IntervalUnit);
}

public sealed record SubscriptionDto(
    long Id,
    long CustomerId,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset CreatedAt)
{
    public static SubscriptionDto From(BillingSubscription subscription) => new(
        subscription.Id,
        subscription.CustomerId,
        subscription.ProductHandle,
        subscription.ProductName,
        subscription.PriceInCents,
        subscription.Interval,
        subscription.IntervalUnit,
        subscription.State,
        subscription.NextBillingAt,
        subscription.CreatedAt);
}

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);

public sealed record CreateSubscriptionResponse(SubscriptionDto Subscription, bool Created);
