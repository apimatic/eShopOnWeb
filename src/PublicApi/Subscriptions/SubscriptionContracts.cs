using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanResponse
{
    public List<SubscriptionPlanDto> Plans { get; init; } = [];
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
}

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public int? Id { get; init; }
    public string? Reference { get; init; }
    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public long? PriceInCents { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
}

public sealed class MySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; init; } = [];
}
