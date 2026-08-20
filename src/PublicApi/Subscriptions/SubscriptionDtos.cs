using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionDto(
    long Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class SubscribeRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed class SubscribeResponse
{
    public required SubscriptionDto Subscription { get; init; }
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
