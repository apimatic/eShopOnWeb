using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionPlanResponse
{
    public IReadOnlyList<SubscriptionPlan> Plans { get; init; } = Array.Empty<SubscriptionPlan>();
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionSummary> Subscriptions { get; init; } = Array.Empty<SubscriptionSummary>();
}

public sealed class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}

public sealed class SubscriptionSummary
{
    public int Id { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
}

public sealed class SubscribeResponse
{
    public bool AlreadySubscribed { get; init; }
    public SubscriptionSummary Subscription { get; init; } = new();
}
