using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed class ListSubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class CreateSubscriptionResponse
{
    public required SubscriptionDto Subscription { get; init; }
}

public sealed class ListMySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new(
        plan.Handle,
        plan.Name,
        plan.Description,
        plan.PriceInCents,
        plan.Interval,
        plan.IntervalUnit,
        plan.RequiresPaymentMethod);
}

public sealed record SubscriptionDto(
    long Id,
    string State,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    DateTimeOffset? NextBillingAt)
{
    public static SubscriptionDto From(BillingSubscription subscription) => new(
        subscription.Id,
        subscription.State,
        subscription.ProductHandle,
        subscription.ProductName,
        subscription.PriceInCents,
        subscription.Interval,
        subscription.IntervalUnit,
        subscription.NextBillingAt);
}
