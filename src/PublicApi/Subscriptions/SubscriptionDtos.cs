using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool RequiresPaymentMethod { get; init; }
    public bool Taxable { get; init; }
}

public sealed class SubscriptionDto
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionPlansResponse() { }
    public List<SubscriptionPlanDto> Plans { get; init; } = new();
}

public sealed class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }
    public SubscriptionDto Subscription { get; init; } = null!;
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }
    public List<SubscriptionDto> Subscriptions { get; init; } = new();
}
