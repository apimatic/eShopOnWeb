using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan, mapped from a Maxio product.
/// </summary>
public record SubscriptionPlanDto
{
    /// <summary>Maxio product handle — stable identifier used to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period, in major currency units.</summary>
    public decimal Price { get; set; }

    /// <summary>Number of intervals between billings (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Interval unit: "day" or "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;
}

/// <summary>
/// A Maxio subscription belonging to the authenticated user.
/// </summary>
public record SubscriptionDto
{
    public int Id { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price per billing period, in major currency units.</summary>
    public decimal Price { get; set; }

    /// <summary>Maxio subscription state (active, canceled, past_due, ...).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>When Maxio will next assess/bill this subscription.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>
/// List the subscription plans available for purchase.
/// </summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }

    public ListSubscriptionPlansResponse() { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan (Maxio product handle) to subscribe to.</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>True when the user already had an active subscription to this plan and it was returned instead of creating a new one.</summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>True when a Maxio customer record was created for the user as part of this call.</summary>
    public bool CustomerCreated { get; set; }
}

/// <summary>
/// List the authenticated user's Maxio subscriptions.
/// </summary>
public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public GetMySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}