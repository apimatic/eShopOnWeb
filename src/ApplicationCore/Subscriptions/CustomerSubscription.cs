using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription that a Maxio customer currently holds, as surfaced to the eShopOnWeb shopper.
/// </summary>
public class CustomerSubscription
{
    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; init; }

    /// <summary>The subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Handle of the plan (Maxio product) this subscription is for, when available.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Human-readable plan name, when available.</summary>
    public string? PlanName { get; init; }

    /// <summary>The recurring price currently charged for this subscription, in integer cents.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>The recurring price in the currency's major unit (e.g. dollars).</summary>
    public decimal? PriceInDollars => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>Start of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>End of the current billing period (i.e. the next scheduled charge).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next payment capture will be attempted. Usually tracks <see cref="CurrentPeriodEndsAt"/>.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>When the subscription became active (came out of trial, or began if no trial).</summary>
    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
