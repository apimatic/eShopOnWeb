using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to a billing customer. Provider-agnostic projection.
/// </summary>
public class CustomerSubscription
{
    public int Id { get; init; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Handle of the subscribed plan/product, when known.</summary>
    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring price of the subscribed product, in integer cents.</summary>
    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next billing/charge attempt is scheduled.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// True when the subscription is in a state that should block creating a duplicate enrollment
    /// for the same plan (i.e. it is live or otherwise not at end-of-life).
    /// </summary>
    public bool IsLive => State is
        "active" or "trialing" or "assessing" or "pending" or "paused" or
        "past_due" or "soft_failure" or "unpaid" or "on_hold" or
        "suspended" or "awaiting_signup";
}
