using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string? ProductPricePointHandle { get; init; }
    public bool RequiresPaymentMethod { get; init; }
}

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionPlansResponse() { }
    public List<SubscriptionPlanDto> Plans { get; init; } = new();
}

public sealed class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public int? Id { get; init; }
    public string? Reference { get; init; }
    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public string? State { get; init; }
    public long? PriceInCents { get; init; }
    public long? BillingAmountInCents { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
}

public sealed class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }
    public SubscriptionDto Subscription { get; init; } = new();
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }
    public List<SubscriptionDto> Subscriptions { get; init; } = new();
}
