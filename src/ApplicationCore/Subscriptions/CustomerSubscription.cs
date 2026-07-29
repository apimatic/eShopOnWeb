using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a plan, projected from a Maxio subscription. Carries the
/// facts we confirm back to the user after subscribing: plan, price, state and next
/// billing date.
/// </summary>
public sealed class CustomerSubscription
{
    public long Id { get; init; }

    public long CustomerId { get; init; }

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    /// <summary>Maxio subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public required string State { get; init; }

    public int PriceInCents { get; init; }

    public int Interval { get; init; }

    public required string IntervalUnit { get; init; }

    /// <summary>When the current billing period began.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>When the next charge/renewal is scheduled — the "next billing date" (Maxio <c>current_period_ends_at</c>).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>How payment is collected, e.g. <c>remittance</c> (invoice) or <c>automatic</c> (card on file).</summary>
    public required string PaymentCollectionMethod { get; init; }

    /// <summary>
    /// True when this subscription already existed and was returned by the idempotency
    /// guard rather than newly created by the current request (e.g. a double-click).
    /// </summary>
    public bool AlreadyExisted { get; init; }

    public string FormattedPrice =>
        $"{(PriceInCents / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US"))} / {(Interval == 1 ? IntervalUnit : $"{Interval} {IntervalUnit}s")}";
}
