using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanResponse
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}

public sealed class SubscriptionPlanListResponse
{
    public IReadOnlyList<SubscriptionPlanResponse> Plans { get; init; } = Array.Empty<SubscriptionPlanResponse>();
}

public sealed class CreateSubscriptionRequest
{
    /// <summary>The plan handle returned from GET /api/subscription-plans.</summary>
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionResponse
{
    public long Id { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionResponse> Subscriptions { get; init; } = Array.Empty<SubscriptionResponse>();
}
