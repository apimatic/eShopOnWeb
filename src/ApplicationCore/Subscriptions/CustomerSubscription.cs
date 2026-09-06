using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as held by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; init; }

    /// <summary>
    /// Lifecycle state reported by the billing system, e.g. "active", "trialing", "past_due", "canceled".
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring price of the subscription in the smallest currency unit.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, e.g. 299.00.</summary>
    public decimal Price => PriceInCents / 100m;

    public string Currency { get; init; } = string.Empty;

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>
    /// When the billing system will next attempt to bill the shopper. Null once the subscription
    /// has reached an end-of-life state.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Identifier of the owning customer in the billing system.</summary>
    public long CustomerId { get; init; }

    /// <summary>The eShopOnWeb-side reference stored against the billing customer.</summary>
    public string? CustomerReference { get; init; }
}
