using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public long MaxioSubscriptionId { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public decimal Price { get; init; }
    public string? Currency { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
}

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}

public sealed class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public sealed class SubscribeResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; init; }
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; } = new();
}
