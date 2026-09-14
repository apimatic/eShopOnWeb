using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to an eShopOnWeb user, as recorded in Maxio Advanced Billing.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        int id,
        string state,
        string planHandle,
        string planName,
        int priceInCents,
        string currency,
        string intervalUnit,
        int intervalCount,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingDate,
        DateTimeOffset? activatedAt,
        DateTimeOffset? createdAt,
        string? paymentCollectionMethod,
        int customerId,
        string? customerReference)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        IntervalUnit = intervalUnit;
        IntervalCount = intervalCount;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingDate = nextBillingDate;
        ActivatedAt = activatedAt;
        CreatedAt = createdAt;
        PaymentCollectionMethod = paymentCollectionMethod;
        CustomerId = customerId;
        CustomerReference = customerReference;
    }

    /// <summary>Maxio subscription id.</summary>
    public int Id { get; }

    /// <summary>Subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; }

    public string PlanHandle { get; }
    public string PlanName { get; }

    public int PriceInCents { get; }
    public decimal Price => PriceInCents / 100m;
    public string Currency { get; }

    public string IntervalUnit { get; }
    public int IntervalCount { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the next charge will be assessed (Maxio next_assessment_at).</summary>
    public DateTimeOffset? NextBillingDate { get; }

    public DateTimeOffset? ActivatedAt { get; }
    public DateTimeOffset? CreatedAt { get; }

    public string? PaymentCollectionMethod { get; }

    public int CustomerId { get; }
    public string? CustomerReference { get; }
}
