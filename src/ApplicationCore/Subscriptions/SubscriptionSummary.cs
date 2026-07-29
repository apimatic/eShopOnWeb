using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A snapshot of a Maxio subscription, projected to the fields the storefront cares about.
/// </summary>
public record SubscriptionSummary
{
    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; init; }

    /// <summary>The Maxio subscription state (e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public string State { get; init; } = string.Empty;

    public string? ProductHandle { get; init; }

    public string? ProductName { get; init; }

    /// <summary>The recurring product price for this subscription, in integer cents.</summary>
    public long ProductPriceInCents { get; init; }

    /// <summary>Interval unit for the subscribed product (<c>day</c> or <c>month</c>).</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>
    /// End of the current billing period — i.e. the next scheduled renewal / billing date.
    /// </summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When Maxio will next attempt to capture payment (usually tracks the period end).</summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Convenience projection of <see cref="ProductPriceInCents"/> to whole currency units.</summary>
    public decimal ProductPrice => ProductPriceInCents / 100m;

    /// <summary>The next billing date presented to the shopper.</summary>
    public DateTimeOffset? NextBillingAt => CurrentPeriodEndsAt;
}
