using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription, as reported by the billing system.
/// </summary>
public class Subscription
{
    public Subscription(
        long id,
        string? reference,
        string state,
        string planHandle,
        string planName,
        long priceInCents,
        string currency,
        int interval,
        string intervalUnit,
        long balanceInCents,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? activatedAt,
        DateTimeOffset? canceledAt,
        long customerId,
        string? customerReference)
    {
        Id = id;
        Reference = reference;
        State = Guard.Against.NullOrWhiteSpace(state, nameof(state));
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        BalanceInCents = balanceInCents;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        ActivatedAt = activatedAt;
        CanceledAt = canceledAt;
        CustomerId = customerId;
        CustomerReference = customerReference;
    }

    /// <summary>Billing system identifier for the subscription.</summary>
    public long Id { get; }

    /// <summary>The reference eShopOnWeb assigned to this subscription; unique within the billing site.</summary>
    public string? Reference { get; }

    /// <summary>One of <see cref="SubscriptionStates"/>.</summary>
    public string State { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public long PriceInCents { get; }

    public string Currency { get; }

    public int Interval { get; }

    public string IntervalUnit { get; }

    /// <summary>Amount currently owed on the subscription, in minor units.</summary>
    public long BalanceInCents { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the billing system will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? ActivatedAt { get; }

    public DateTimeOffset? CanceledAt { get; }

    public long CustomerId { get; }

    public string? CustomerReference { get; }

    public bool IsLive => SubscriptionStates.IsLive(State);

    public decimal Price => PriceInCents / 100m;
}
