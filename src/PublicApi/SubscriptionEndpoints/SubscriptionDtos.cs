using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan offered by the store (a Maxio product in the configured product family).
/// </summary>
public class SubscriptionPlanDto
{
    public int MaxioProductId { get; set; }
    public string Handle { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequiresPaymentProfile { get; set; }
}

/// <summary>
/// A subscription an eShopOnWeb user holds at the billing provider.
/// </summary>
public class SubscriptionDto
{
    public int MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = default!;
    public string? PlanName { get; set; }
    public long PriceInCents { get; set; }
    public string State { get; set; } = default!;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>
/// Response for listing the subscription plans.
/// </summary>
public class SubscriptionPlanListResponse : BaseResponse
{
    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class SubscriptionCreateRequest : BaseRequest
{
    public string PlanHandle { get; set; } = default!;
}

/// <summary>
/// Response after subscribing to a plan.
/// </summary>
public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionCreateResponse() { }

    public SubscriptionDto? Subscription { get; set; }
}

/// <summary>
/// Response listing the authenticated user's subscriptions.
/// </summary>
public class MySubscriptionsListResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}