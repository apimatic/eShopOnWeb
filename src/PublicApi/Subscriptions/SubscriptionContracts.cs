using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? Currency);

public sealed record SubscriptionDto(
    int Id,
    string? ProductHandle,
    string? ProductName,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate);

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
