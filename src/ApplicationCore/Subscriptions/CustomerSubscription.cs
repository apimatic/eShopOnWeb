using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription on file for a shopper, as reported by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public long Id { get; init; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price in cents for the subscribed plan.</summary>
    public long PriceInCents { get; init; }

    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>
    /// When the next regularly scheduled charge will occur (Maxio current_period_ends_at,
    /// falling back to next_assessment_at). Null for end-of-life states.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public long CustomerId { get; init; }

    public string? CustomerReference { get; init; }

    /// <summary>
    /// True when the subscription is in a state that entitles the shopper to the service
    /// (active, trialing, assessing, pending). Used to enforce subscribe idempotency.
    /// </summary>
    public bool IsLive =>
        State is "active" or "trialing" or "assessing" or "pending";
}
