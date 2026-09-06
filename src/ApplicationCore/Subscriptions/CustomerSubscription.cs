using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(long id, string state, string planHandle, string planName,
        long priceInCents, string currency, int? intervalLength, string? intervalUnit,
        DateTimeOffset? currentPeriodStartedAt, DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt, DateTimeOffset? activatedAt, DateTimeOffset? canceledAt,
        long balanceInCents, string? paymentCollectionMethod,
        long billingCustomerId, string? billingCustomerReference)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        IntervalLength = intervalLength;
        IntervalUnit = intervalUnit;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        ActivatedAt = activatedAt;
        CanceledAt = canceledAt;
        BalanceInCents = balanceInCents;
        PaymentCollectionMethod = paymentCollectionMethod;
        BillingCustomerId = billingCustomerId;
        BillingCustomerReference = billingCustomerReference;
    }

    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public long PriceInCents { get; }

    public decimal Price => decimal.Divide(PriceInCents, 100m);

    public string Currency { get; }

    public int? IntervalLength { get; }

    public string? IntervalUnit { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the next renewal is assessed. Null once the subscription reaches end of life.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? ActivatedAt { get; }

    public DateTimeOffset? CanceledAt { get; }

    /// <summary>Outstanding balance on the subscription, in minor currency units.</summary>
    public long BalanceInCents { get; }

    /// <summary>How the balance is collected, e.g. "automatic" (card on file) or "remittance" (invoice).</summary>
    public string? PaymentCollectionMethod { get; }

    public long BillingCustomerId { get; }

    /// <summary>The reference the billing customer is keyed by - derived from the eShopOnWeb user name.</summary>
    public string? BillingCustomerReference { get; }

    /// <summary>
    /// True while the subscription still entitles the shopper to the plan. Mirrors the billing system's
    /// "live" states; problem states such as past_due keep entitlement until dunning ends it.
    /// </summary>
    public bool IsLive => SubscriptionStates.IsLive(State);
}
