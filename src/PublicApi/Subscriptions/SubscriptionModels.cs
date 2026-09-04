using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscribeRequest(string? PlanHandle);

public sealed record SubscriptionPlanDto(
    string Handle,
    string? Name,
    long? PriceInCents,
    decimal? Price,
    int? Interval,
    string? IntervalUnit,
    string? PricePointHandle,
    string? PricePointName);

public sealed record SubscriptionDto(
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    decimal? Price,
    int? Interval,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> SubscriptionPlans);

public sealed record SubscriptionResponse(SubscriptionDto Subscription);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
