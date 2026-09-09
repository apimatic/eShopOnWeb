using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available for purchase.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable Maxio product handle, used as the subscribe target.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price in cents.
    /// </summary>
    public int PriceInCents { get; set; }

    /// <summary>
    /// Formatted recurring price, e.g. "$299.00".
    /// </summary>
    public string Price { get; set; } = string.Empty;

    /// <summary>
    /// Billing interval unit, e.g. "month".
    /// </summary>
    public string? IntervalUnit { get; set; }

    /// <summary>
    /// Number of <see cref="IntervalUnit"/> between billings.
    /// </summary>
    public int Interval { get; set; }
}

/// <summary>
/// The reconciled state of one of the caller's subscriptions.
/// </summary>
public class SubscriptionSummaryDto
{
    public int MaxioSubscriptionId { get; set; }

    public int MaxioCustomerId { get; set; }

    /// <summary>
    /// Stable Maxio product handle of the subscribed plan.
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Recurring price in cents.
    /// </summary>
    public int PriceInCents { get; set; }

    /// <summary>
    /// Formatted recurring price, e.g. "$299.00".
    /// </summary>
    public string Price { get; set; } = string.Empty;

    /// <summary>
    /// Maxio subscription state, e.g. "active" or "canceled".
    /// </summary>
    public string State { get; set; } = string.Empty;

    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>
    /// When the next automatic billing occurs.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}

/// <summary>
/// Response for GET /api/subscription-plans.
/// </summary>
public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionPlanListResponse() { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Request for POST /api/subscriptions.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio product handle of the plan to subscribe to.
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}

/// <summary>
/// Response for POST /api/subscriptions.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public CreateSubscriptionResponse() { }

    public SubscriptionSummaryDto? Subscription { get; set; }
}

/// <summary>
/// Response for GET /api/my-subscriptions.
/// </summary>
public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public MySubscriptionsResponse() { }

    public List<SubscriptionSummaryDto> Subscriptions { get; set; } = new();
}
