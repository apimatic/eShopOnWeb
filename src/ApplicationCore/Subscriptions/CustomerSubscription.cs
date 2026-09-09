using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription that belongs to an eShopOnWeb user, as reported by Maxio (the system of
/// record). Prices and dates reflect Maxio's current state, not a local copy.
/// </summary>
public sealed class CustomerSubscription
{
    /// <summary>The Maxio subscription id.</summary>
    public long Id { get; init; }

    /// <summary>The Maxio subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public required string State { get; init; }

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    /// <summary>The recurring amount for this subscription, in cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Human-readable price, e.g. <c>$299.00</c>.</summary>
    public required string FormattedPrice { get; init; }

    public string Currency { get; init; } = "USD";

    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }

    /// <summary>
    /// When the current period ends and the next regularly scheduled charge occurs — i.e.
    /// the next billing date.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
