using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan, as exposed by the billing provider.
/// </summary>
public sealed class SubscriptionPlanDto
{
    /// <summary>
    /// Stable plan handle (use this to subscribe).
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price in cents for one billing period.
    /// </summary>
    public long? PriceInCents { get; set; }

    /// <summary>
    /// Number of intervals per billing period (e.g. 1).
    /// </summary>
    public int? Interval { get; set; }

    /// <summary>
    /// Billing interval unit (e.g. "month").
    /// </summary>
    public string? IntervalUnit { get; set; }

    /// <summary>
    /// Whether the plan requires a payment card at signup.
    /// </summary>
    public bool? RequireCreditCard { get; set; }

    /// <summary>
    /// Whether the plan requests (but does not require) a payment card at signup.
    /// </summary>
    public bool? RequestCreditCard { get; set; }
}

/// <summary>
/// A subscription belonging to the current user.
/// </summary>
public sealed class SubscriptionDto
{
    public int? Id { get; set; }

    /// <summary>
    /// Handle of the subscribed plan.
    /// </summary>
    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>
    /// Recurring plan price in cents.
    /// </summary>
    public long? PriceInCents { get; set; }

    /// <summary>
    /// Subscription state (e.g. "active").
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    /// When the current billing period ends / next billing date.
    /// </summary>
    public DateTimeOffset? NextBillingDateUtc { get; set; }
}
