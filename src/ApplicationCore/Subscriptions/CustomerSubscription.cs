using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as it currently stands in the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        long id,
        string? reference,
        string state,
        string? planHandle,
        string? planName,
        long planPriceInCents,
        string currency,
        int? interval,
        string? intervalUnit,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? activatedAt,
        DateTimeOffset? canceledAt,
        long balanceInCents,
        string? paymentCollectionMethod,
        long customerId,
        string? customerReference)
    {
        Id = id;
        Reference = reference;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PlanPriceInCents = planPriceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        ActivatedAt = activatedAt;
        CanceledAt = canceledAt;
        BalanceInCents = balanceInCents;
        PaymentCollectionMethod = paymentCollectionMethod;
        CustomerId = customerId;
        CustomerReference = customerReference;
    }

    public long Id { get; }

    /// <summary>The eShopOnWeb-owned idempotency anchor for this subscription.</summary>
    public string? Reference { get; }

    /// <summary>Billing-system state, e.g. active / trialing / past_due / canceled.</summary>
    public string State { get; }

    public string? PlanHandle { get; }

    public string? PlanName { get; }

    public long PlanPriceInCents { get; }

    public string Currency { get; }

    public int? Interval { get; }

    public string? IntervalUnit { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the next charge is expected. Tracks the period end unless a renewal is being retried.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? ActivatedAt { get; }

    public DateTimeOffset? CanceledAt { get; }

    public long BalanceInCents { get; }

    public string? PaymentCollectionMethod { get; }

    public long CustomerId { get; }

    public string? CustomerReference { get; }

    /// <summary>
    /// True while the subscription is still a going concern. End-of-life subscriptions are not
    /// reused when a shopper subscribes to the same plan again.
    /// </summary>
    public bool IsLive => SubscriptionStates.IsLive(State);
}
