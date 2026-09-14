using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionDto(
    long Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed class ListSubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class ListMySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class CreateSubscriptionResponse
{
    public required SubscriptionDto Subscription { get; init; }
}

internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this BillingPlan plan) => new(
        plan.Handle,
        plan.Name,
        plan.Description,
        plan.PriceInCents,
        plan.Interval,
        plan.IntervalUnit,
        plan.RequiresPaymentMethod);

    public static SubscriptionDto ToDto(this BillingSubscription subscription) => new(
        subscription.Id,
        subscription.ProductHandle,
        subscription.ProductName,
        subscription.PriceInCents,
        subscription.Interval,
        subscription.IntervalUnit,
        subscription.Currency,
        subscription.State,
        subscription.NextBillingAt);
}
