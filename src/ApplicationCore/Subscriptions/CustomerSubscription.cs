using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to an eShopOnWeb shopper, as recorded in Maxio
/// (the billing system of record).
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        long id,
        string state,
        string planHandle,
        string planName,
        int priceInCents,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? activatedAt,
        string paymentCollectionMethod)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        ActivatedAt = activatedAt;
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    /// <summary>The Maxio subscription id.</summary>
    public long Id { get; }

    /// <summary>Maxio subscription state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    /// <summary>The recurring product price for this subscription, in integer cents.</summary>
    public int PriceInCents { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the next billing/assessment will occur (Maxio next_assessment_at).</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? ActivatedAt { get; }

    public string PaymentCollectionMethod { get; }

    /// <summary>
    /// True when the subscription is in a live (non end-of-life) state and therefore
    /// counts as an active enrollment for idempotency purposes.
    /// </summary>
    public bool IsLive => SubscriptionStates.IsLive(State);
}
