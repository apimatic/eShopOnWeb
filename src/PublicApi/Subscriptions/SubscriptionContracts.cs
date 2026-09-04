using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool RequiresPaymentMethod { get; init; }
    public bool Taxable { get; init; }
}

public sealed class CreateSubscriptionRequest : BaseRequest
{
    public string? PlanHandle { get; init; }
}

public sealed class SubscriptionResponse : BaseResponse
{
    public SubscriptionDto Subscription { get; init; } = new();
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; } = new();
}

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
}
