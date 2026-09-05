using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionPlanResponse
{
    public List<SubscriptionPlanDto> Plans { get; init; } = new();
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
}

public sealed class MySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; init; } = new();
}

public sealed class SubscriptionDto
{
    public int Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int? PriceInCents { get; init; }
    public string? Currency { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
}
