using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan (a Maxio product in the configured product family).
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// The Maxio product id. Note that Maxio may reassign ids; prefer the stable handle.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The stable Maxio product handle.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price in major currency units (e.g. dollars).
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Number of intervals between renewals (e.g. 1 with IntervalUnit "month" = monthly).
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// Interval unit: "month" or "day".
    /// </summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Whether the plan requires a payment method at signup.
    /// </summary>
    public bool RequireCreditCard { get; set; }
}

/// <summary>
/// A subscription owned by the authenticated user, as recorded in Maxio.
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// The Maxio subscription id.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The Maxio subscription state (active, trialing, past_due, canceled, ...).
    /// </summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>
    /// Recurring price in major currency units.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// When the current billing period ends (the next regularly scheduled charge).
    /// </summary>
    public string? NextBillingDate { get; set; }

    public string? ActivatedAt { get; set; }

    public string? CreatedAt { get; set; }

    public string? CanceledAt { get; set; }

    /// <summary>
    /// The Maxio customer id owning this subscription.
    /// </summary>
    public int CustomerId { get; set; }
}

/// <summary>Response for GET /api/subscription-plans.</summary>
public class ListSubscriptionPlansResponse
{
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}

/// <summary>Response for POST /api/subscriptions.</summary>
public class CreateSubscriptionResponse
{
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// True when the subscription already existed (idempotent repeat of the same signup).
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    public SubscriptionDto Subscription { get; set; } = new();
}

/// <summary>Response for GET /api/my-subscriptions.</summary>
public class ListMySubscriptionsResponse
{
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
