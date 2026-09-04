using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string? PricePointHandle { get; init; }
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

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionPlansResponse() { }
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; init; } = string.Empty;
    public string? ProductPricePointHandle { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed class SubscriptionResponse : BaseResponse
{
    public SubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionResponse() { }
    public SubscriptionDto Subscription { get; init; } = new();
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
