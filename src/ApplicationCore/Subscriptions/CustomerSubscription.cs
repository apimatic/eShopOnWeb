using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A customer's enrollment in a plan, as reported by the billing system of record.
/// </summary>
public record CustomerSubscription
{
    /// <summary>The subscription's unique id in the billing system.</summary>
    public long Id { get; init; }

    /// <summary>Lifecycle state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>The recurring price the subscription is currently billed at, in cents.</summary>
    public long PriceInCents { get; init; }

    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next charge is scheduled (the next billing date confirmed back to the user).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>The billing system's customer id that owns this subscription.</summary>
    public long CustomerId { get; init; }

    /// <summary>The application-owned reference stored on the billing customer.</summary>
    public string? CustomerReference { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
