using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanDto
{
    public string ProductHandle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string? ProductPricePointHandle { get; init; }
    public string? ProductPricePointName { get; init; }
}

public sealed class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse() { }
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }
    public List<SubscriptionPlanDto> Plans { get; init; } = new();
}

public sealed class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; init; } = string.Empty;
    public string? ProductPricePointHandle { get; init; }
}

public sealed class SubscriptionDto
{
    public int? SubscriptionId { get; init; }
    public string? Reference { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long? PriceInCents { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public string? ProductPricePointHandle { get; init; }
    public string? ProductPricePointName { get; init; }
}

public sealed class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionDto Subscription { get; init; } = new();
}

public sealed class SubscriptionListResponse : BaseResponse
{
    public SubscriptionListResponse() { }
    public SubscriptionListResponse(Guid correlationId) : base(correlationId) { }
    public List<SubscriptionDto> Subscriptions { get; init; } = new();
}
