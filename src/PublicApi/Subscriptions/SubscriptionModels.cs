using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public sealed class SubscriptionPlanResponse
{
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
}

public sealed class SubscriptionResponse
{
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long? PriceInCents { get; init; }
    public string? Currency { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public string Reference { get; init; } = string.Empty;
}

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanResponse> Plans { get; init; } = Array.Empty<SubscriptionPlanResponse>();
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionResponse> Subscriptions { get; init; } = Array.Empty<SubscriptionResponse>();
}
