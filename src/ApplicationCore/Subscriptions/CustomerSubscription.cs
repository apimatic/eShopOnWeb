using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing system.
/// </summary>
public class CustomerSubscription
{
    public int Id { get; init; }

    /// <summary>
    /// The reference eShopOnWeb assigned at signup. Unique per billing site, which is what makes
    /// subscribe idempotent across processes.
    /// </summary>
    public string? Reference { get; init; }

    /// <summary>Raw billing-system state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True while the subscription still occupies the plan (see <see cref="SubscriptionStates.IsLive"/>).</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    /// <summary>True while the shopper should have access to the paid service.</summary>
    public bool GrantsEntitlement => SubscriptionStates.GrantsEntitlement(State);

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    public int? PlanId { get; init; }

    /// <summary>
    /// Recurring amount this subscription is billed. Can differ from the plan's current list price
    /// when the plan has been re-priced since signup.
    /// </summary>
    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string Currency { get; init; } = string.Empty;

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// When the billing system will next attempt to collect. This tracks the period end except
    /// while a failed payment is being retried, which is why it - and not the period end - is the
    /// next billing date reported to the shopper.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public long BalanceInCents { get; init; }

    public decimal Balance => BalanceInCents / 100m;

    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Billing-system customer id backing this subscription.</summary>
    public int CustomerId { get; init; }

    /// <summary>The reference eShopOnWeb assigned to the billing customer, derived from the eShopOnWeb user.</summary>
    public string? CustomerReference { get; init; }
}
