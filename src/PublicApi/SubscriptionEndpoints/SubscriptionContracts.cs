using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeRequest
{
    /// <summary>Optional Maxio product handle. If omitted, the first available family plan is used.</summary>
    public string? ProductHandle { get; set; }
}

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanResponse> Plans { get; init; } = Array.Empty<SubscriptionPlanResponse>();
}

public sealed record SubscriptionPlanResponse(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionResponse> Subscriptions { get; init; } = Array.Empty<SubscriptionResponse>();
}

public sealed record SubscriptionResponse(
    int Id,
    string? PlanHandle,
    string? PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);
