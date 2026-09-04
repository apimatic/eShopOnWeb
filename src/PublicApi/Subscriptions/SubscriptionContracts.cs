using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool RequiresPaymentMethod { get; init; }
}

public sealed class SubscriptionPlanListResponse : BaseResponse
{
    public List<SubscriptionPlan> Plans { get; init; } = new();
}

public sealed class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionDetails
{
    public int Id { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
}

public sealed class SubscribeResponse : BaseResponse
{
    public SubscriptionDetails Subscription { get; init; } = new();
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDetails> Subscriptions { get; init; } = new();
}
