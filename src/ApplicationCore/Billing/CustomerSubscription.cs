using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's enrolment in a plan — an eShopOnWeb-facing projection of a Maxio subscription.
/// </summary>
public class CustomerSubscription
{
    public int? SubscriptionId { get; init; }

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>Current recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Decimal price derived from <see cref="PriceInCents"/> (cents / 100).</summary>
    public decimal Price { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>Subscription state wire value (e.g. <c>active</c>, <c>trialing</c>).</summary>
    public string State { get; init; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>
    /// When the current billing period ends — i.e. the next billing date. (Maxio does not return
    /// a separate <c>next_billing_at</c>; <c>current_period_ends_at</c> is that date.)
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>
    /// True when this subscription already existed (a live subscription for the same customer+plan
    /// was found) and the subscribe call returned it instead of creating a new one. Lets the API
    /// distinguish a fresh enrolment (201) from an idempotent no-op (200).
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
