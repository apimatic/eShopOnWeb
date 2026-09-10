using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as it exists in Maxio (the billing system of record).
/// </summary>
public sealed class CustomerSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public long Id { get; init; }

    /// <summary>Maxio subscription state (e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public required string State { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring price for the subscribed product, in the smallest currency unit.</summary>
    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string? IntervalUnit { get; init; }

    public int IntervalCount { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>
    /// When the subscription will next be billed (Maxio <c>current_period_ends_at</c>) — i.e.
    /// the next-billing-date confirmed back to the shopper.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// True when this subscription was already present and returned as-is rather than newly
    /// created — surfaced so callers/tests can observe idempotent re-subscribe behaviour.
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
