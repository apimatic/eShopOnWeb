using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDto(
    int Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed class SubscriptionPlanListResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class SubscribeRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class SubscribeResponse
{
    public required SubscriptionDto Subscription { get; init; }
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}

internal static class SubscriptionDtoMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) =>
        new(plan.Handle, plan.Name, plan.PriceInCents, plan.Interval, plan.IntervalUnit);

    public static SubscriptionDto ToDto(this BillingSubscription subscription) =>
        new(
            subscription.Id,
            subscription.Reference,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.Interval,
            subscription.IntervalUnit,
            subscription.State,
            subscription.NextBillingDate);

    public static IReadOnlyList<SubscriptionDto> ToDtos(this IReadOnlyList<BillingSubscription> subscriptions) =>
        subscriptions.Select(ToDto).ToArray();
}
