using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrolment on a <see cref="SubscriptionPlan"/>, as reported by the billing provider.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Provider-assigned subscription id.</summary>
    public int Id { get; init; }

    /// <summary>The reference eShopOnWeb assigned to the subscription; it is what makes signup idempotent.</summary>
    public string? Reference { get; init; }

    /// <summary>One of <see cref="SubscriptionStates"/>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True while the enrolment exists (see <see cref="SubscriptionStates.IsLive"/>).</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string? Currency { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// When the provider will next attempt to bill this subscription. Usually tracks
    /// <see cref="CurrentPeriodEndsAt"/> but diverges while a failed payment is being retried.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public bool CancelAtEndOfPeriod { get; init; }

    /// <summary>Outstanding balance on the subscription, in the smallest currency unit.</summary>
    public long BalanceInCents { get; init; }

    public string? PaymentCollectionMethod { get; init; }

    public int CustomerId { get; init; }

    public string? CustomerReference { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
