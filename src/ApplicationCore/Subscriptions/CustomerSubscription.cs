using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reported by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public int Id { get; init; }

    /// <summary>Provider subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public required string State { get; init; }

    /// <summary>True while the subscription still entitles the shopper (i.e. it has not reached end of life).</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    public long PriceInCents { get; init; }
    public string FormattedPrice => (PriceInCents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    public string? Currency { get; init; }

    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next attempt to bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public long BalanceInCents { get; init; }
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>The reference this integration wrote when the subscription was created, when one was used.</summary>
    public string? Reference { get; init; }

    public BillingCustomer? Customer { get; init; }
}
