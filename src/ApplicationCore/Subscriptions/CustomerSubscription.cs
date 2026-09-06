using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment on a <see cref="SubscriptionPlan"/>, as reported by the billing
/// system of record. eShopOnWeb never owns this state; it always reflects the provider.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Identifier assigned by the billing system.</summary>
    public int? Id { get; init; }

    /// <summary>The deterministic reference this application supplied at signup.</summary>
    public string? Reference { get; init; }

    /// <summary>Lifecycle state reported by the billing system (for example "active").</summary>
    public string? State { get; init; }

    /// <summary>True while the subscription is anything other than finally ended.</summary>
    public bool IsLive { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the billing system will next assess this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }
}
