using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed record SubscriptionPlanResponse(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionResponse(
    long SubscriptionId,
    string ProductHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed class MySubscriptionsResponse
{
    public List<SubscriptionResponse> Subscriptions { get; } = new();
}
