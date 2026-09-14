using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reported by the billing provider.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Provider-assigned subscription id.</summary>
    public long Id { get; init; }

    /// <summary>Provider state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// True while the subscription has not reached a terminal state. Used to decide whether a repeat
    /// subscribe request is a duplicate of an existing enrollment.
    /// </summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>The recurring amount actually being billed, in the smallest currency unit.</summary>
    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string Currency { get; init; } = string.Empty;

    public int? Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// When the provider will next attempt to bill this subscription. Tracks the end of the current
    /// period except when a renewal payment failed and a retry has been scheduled.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public long BalanceInCents { get; init; }

    public long CustomerId { get; init; }

    /// <summary>The provider-side customer reference that maps back to the eShopOnWeb user.</summary>
    public string? CustomerReference { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
