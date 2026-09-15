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
    long PriceInCents,
    decimal Price,
    double? Interval,
    string? IntervalUnit);

public sealed record SubscriptionDto(
    double? Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    decimal? Price,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
