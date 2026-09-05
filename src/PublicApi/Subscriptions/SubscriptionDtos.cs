using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal? Price { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public bool RequiresCreditCard { get; init; }
}

public sealed class SubscriptionDto
{
    public int? Id { get; init; }
    public string? Reference { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string? PlanName { get; init; }
    public decimal? Price { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
}

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> SubscriptionPlans { get; init; } = [];
}

public sealed class MySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; init; } = [];
}
