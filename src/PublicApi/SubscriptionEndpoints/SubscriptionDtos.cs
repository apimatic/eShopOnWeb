using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string ProductHandle,
    string Name,
    string? Description,
    long PriceInCents,
    string Currency,
    int Interval,
    string IntervalUnit)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new(
        plan.ProductHandle,
        plan.Name,
        plan.Description,
        plan.PriceInCents,
        plan.Currency,
        plan.Interval,
        plan.IntervalUnit);
}

public sealed record SubscriptionDto(
    long Id,
    string ProductHandle,
    string PlanName,
    long PriceInCents,
    string Currency,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate)
{
    public static SubscriptionDto From(UserSubscription subscription) => new(
        subscription.Id,
        subscription.ProductHandle,
        subscription.PlanName,
        subscription.PriceInCents,
        subscription.Currency,
        subscription.Interval,
        subscription.IntervalUnit,
        subscription.State,
        subscription.NextBillingDate);
}

public sealed record ListSubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans)
{
    public static ListSubscriptionPlansResponse From(IReadOnlyList<SubscriptionPlan> plans) =>
        new(plans.Select(SubscriptionPlanDto.From).ToList());
}

public sealed record CreateSubscriptionRequest(string ProductHandle);

public sealed record CreateSubscriptionResponse(SubscriptionDto Subscription, bool Created);

public sealed record ListMySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions)
{
    public static ListMySubscriptionsResponse From(IReadOnlyList<UserSubscription> subscriptions) =>
        new(subscriptions.Select(SubscriptionDto.From).ToList());
}
