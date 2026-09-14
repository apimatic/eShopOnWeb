using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to a customer, as reported by Maxio.
/// </summary>
public sealed class CustomerSubscription
{
    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; init; }

    /// <summary>The Maxio subscription state (e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>The handle of the subscribed product/plan, or <c>null</c> for catalog-independent subscriptions.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>The name of the subscribed product/plan.</summary>
    public string? PlanName { get; init; }

    /// <summary>The recurring product price for this subscription, in integer cents.</summary>
    public long ProductPriceInCents { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>
    /// When the next regularly scheduled charge / renewal occurs — surfaced to the user as the next billing date.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>When the current billing period ends.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>The owning Maxio customer id.</summary>
    public int CustomerId { get; init; }

    /// <summary>The owning Maxio customer reference (the eShop user key).</summary>
    public string? CustomerReference { get; init; }
}
