using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a billing plan, as reported back by the billing
/// system. Used both to confirm a freshly created subscription and to list a
/// customer's existing subscriptions.
/// </summary>
public record SubscriptionDetails
{
    public SubscriptionDetails(
        long id,
        string state,
        string planHandle,
        string planName,
        long priceInCents,
        int interval,
        string intervalUnit,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        string? paymentCollectionMethod,
        long customerId,
        string? customerReference,
        bool alreadyExisted)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        PaymentCollectionMethod = paymentCollectionMethod;
        CustomerId = customerId;
        CustomerReference = customerReference;
        AlreadyExisted = alreadyExisted;
    }

    /// <summary>Billing-system subscription id.</summary>
    public long Id { get; }

    /// <summary>Subscription state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    /// <summary>Recurring price for the subscribed product/version, in integer cents.</summary>
    public long PriceInCents { get; }

    public int Interval { get; }

    public string IntervalUnit { get; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the next payment will be assessed (the next billing date).</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public string? PaymentCollectionMethod { get; }

    public long CustomerId { get; }

    public string? CustomerReference { get; }

    /// <summary>
    /// True when this subscription was already present for the customer and was
    /// returned instead of creating a duplicate (idempotent subscribe).
    /// </summary>
    public bool AlreadyExisted { get; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public string FormattedPrice => $"${PriceInCents / 100m:0.00}";
}
