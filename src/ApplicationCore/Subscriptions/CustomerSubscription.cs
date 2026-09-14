using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(long id, string? reference, string state, string planHandle, string planName,
        long priceInCents, string currency, int interval, string intervalUnit,
        DateTimeOffset? currentPeriodStartedAt, DateTimeOffset? currentPeriodEndsAt, DateTimeOffset? nextBillingAt,
        DateTimeOffset? activatedAt, DateTimeOffset? createdAt, long customerId, string? paymentCollectionMethod)
    {
        Id = id;
        Reference = reference;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        ActivatedAt = activatedAt;
        CreatedAt = createdAt;
        CustomerId = customerId;
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; }

    /// <summary>The reference this integration assigned to the subscription when it was created.</summary>
    public string? Reference { get; }

    public string State { get; }

    /// <summary>
    /// True while the subscription still entitles the shopper to the plan. States outside this set are
    /// either end-of-life (canceled, expired, ...) or a failed signup.
    /// </summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string PlanHandle { get; }
    public string PlanName { get; }
    public long PriceInCents { get; }
    public decimal Price => decimal.Divide(PriceInCents, 100m);
    public string Currency { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the next renewal will be assessed.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? ActivatedAt { get; }
    public DateTimeOffset? CreatedAt { get; }
    public long CustomerId { get; }
    public string? PaymentCollectionMethod { get; }
}
