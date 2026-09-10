using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as recorded by the billing
/// system of record (Maxio Advanced Billing). This is a read model — the billing system,
/// not eShopOnWeb, owns the authoritative subscription lifecycle and dates.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Billing-system subscription id.</summary>
    public long Id { get; init; }

    /// <summary>
    /// Lifecycle state as reported by the billing system (e.g. "active", "awaiting_signup",
    /// "canceled"). Kept as the raw value to avoid lossy mapping of provider states.
    /// </summary>
    public string State { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public int PriceInCents { get; init; }

    public string Currency { get; init; } = "USD";

    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>The date the next charge is assessed (Maxio <c>next_assessment_at</c>).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>The stable external reference eShopOnWeb assigned to this subscription.</summary>
    public string? Reference { get; init; }

    /// <summary>The stable external reference of the owning customer (the eShop user key).</summary>
    public string? CustomerReference { get; init; }

    public decimal Price => PriceInCents / 100m;
}
