using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing
/// system of record. eShopOnWeb keeps no local copy - this is always read back from billing.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(int id,
        string? reference,
        string state,
        string? planHandle,
        string? planName,
        int priceInCents,
        int interval,
        string? intervalUnit,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? activatedAt,
        DateTimeOffset? canceledAt,
        string? paymentCollectionMethod,
        int customerId,
        string? customerReference)
    {
        Id = id;
        Reference = reference;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        ActivatedAt = activatedAt;
        CanceledAt = canceledAt;
        PaymentCollectionMethod = paymentCollectionMethod;
        CustomerId = customerId;
        CustomerReference = customerReference;
    }

    public int Id { get; }
    public string? Reference { get; }

    /// <summary>Billing state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; }

    public string? PlanHandle { get; }
    public string? PlanName { get; }
    public int PriceInCents { get; }
    public int Interval { get; }
    public string? IntervalUnit { get; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the next charge will be attempted. Diverges from the period end after a failed renewal.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? ActivatedAt { get; }
    public DateTimeOffset? CanceledAt { get; }
    public string? PaymentCollectionMethod { get; }
    public int CustomerId { get; }
    public string? CustomerReference { get; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>
    /// True while the subscription still entitles the shopper to the plan. Mirrors the billing
    /// system's "live" states; every other state is a problem or end-of-life state.
    /// </summary>
    public bool IsLive => SubscriptionStates.IsLive(State);
}
