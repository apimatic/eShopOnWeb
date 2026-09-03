using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to the current user, projected from a Maxio subscription. Maxio is the system
/// of record; this is a read-model returned to the caller so they can confirm plan/price/state/next-billing.
/// </summary>
public sealed class CustomerSubscription
{
    public int SubscriptionId { get; init; }

    /// <summary>The Maxio subscription <c>reference</c> (deterministic per user+plan; drives idempotency).</summary>
    public string? Reference { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Subscription state wire value, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string? State { get; init; }

    /// <summary>The recurring product price for this subscription, in integer cents.</summary>
    public long? PriceInCents { get; init; }

    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    public string? Currency { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>When the next regularly scheduled charge will occur — the next billing date.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// True when a subscribe call resolved to an already-existing subscription rather than creating a new
    /// one — i.e. an idempotent hit (a double-click, or a retry). False when this call created it.
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
