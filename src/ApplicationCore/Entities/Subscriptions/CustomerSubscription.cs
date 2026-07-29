using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

/// <summary>
/// A subscription that belongs to a customer, projected from a Maxio subscription.
/// </summary>
public record CustomerSubscription
{
    /// <summary>The Maxio subscription id.</summary>
    public required int Id { get; init; }

    /// <summary>Maxio lifecycle state (e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>).</summary>
    public required string State { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>The recurring amount for the subscribed product, in cents.</summary>
    public int ProductPriceInCents { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>
    /// When the next regularly scheduled charge occurs — i.e. the next billing date.
    /// Maps to the Maxio <c>current_period_ends_at</c> field.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Payment collection method in effect (e.g. <c>remittance</c>, <c>automatic</c>).</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>
    /// True when <c>SubscribeAsync</c> returned a pre-existing subscription instead of
    /// creating a new one (idempotent replay of a repeated / double-clicked request).
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
