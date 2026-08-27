using System;
using System.Collections.Generic;

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
    string Reference,
    string ProductHandle,
    string PlanName,
    long PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed class SubscriptionPlanListResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}

public sealed class SubscribeRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class SubscribeResponse
{
    public string Status { get; init; } = string.Empty;
    public string? Code { get; init; }
    public SubscriptionDto? Subscription { get; init; }
}
