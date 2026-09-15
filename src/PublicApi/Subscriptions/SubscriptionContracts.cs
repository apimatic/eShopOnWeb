using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

public sealed record SubscriptionDto(
    int? Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    long? CurrentBillingAmountInCents,
    string? State,
    DateTimeOffset? NextBillingDate);

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> SubscriptionPlans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class SubscribeResponse
{
    public required SubscriptionDto Subscription { get; init; }
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
