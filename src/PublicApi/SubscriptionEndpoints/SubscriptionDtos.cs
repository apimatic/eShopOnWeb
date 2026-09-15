using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool RequiresPaymentMethod { get; init; }
}

public sealed class SubscriptionDto
{
    public int Id { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
}

public sealed class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; init; } = new();
}

public sealed class MySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; init; } = new();
}
