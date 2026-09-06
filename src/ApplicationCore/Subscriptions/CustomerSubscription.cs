using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as it exists in the billing provider, which is the system of record.
/// Nothing about a subscription is persisted locally; every read reflects live provider state.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        long id,
        string? reference,
        string state,
        bool isLive,
        string planHandle,
        string planName,
        long priceInCents,
        string? currency,
        int interval,
        string intervalUnit,
        long balanceInCents,
        string? paymentCollectionMethod,
        long customerId,
        string? customerReference,
        DateTimeOffset? createdAt,
        DateTimeOffset? activatedAt,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? trialEndedAt,
        DateTimeOffset? canceledAt)
    {
        Id = id;
        Reference = reference;
        State = state;
        IsLive = isLive;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        BalanceInCents = balanceInCents;
        PaymentCollectionMethod = paymentCollectionMethod;
        CustomerId = customerId;
        CustomerReference = customerReference;
        CreatedAt = createdAt;
        ActivatedAt = activatedAt;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        TrialEndedAt = trialEndedAt;
        CanceledAt = canceledAt;
    }

    /// <summary>Provider-assigned subscription id.</summary>
    public long Id { get; }

    /// <summary>
    /// The deterministic reference this integration stamps onto the subscription. It is unique
    /// per site (the provider enforces it), which is what makes <c>POST /api/subscriptions</c> idempotent.
    /// </summary>
    public string? Reference { get; }

    /// <summary>Raw provider state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; }

    /// <summary>True when <see cref="State"/> is one that still entitles the shopper to the plan.</summary>
    public bool IsLive { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public long PriceInCents { get; }

    public decimal Price => PriceInCents / 100m;

    public string? Currency { get; }

    public int Interval { get; }

    public string IntervalUnit { get; }

    public long BalanceInCents { get; }

    public decimal Balance => BalanceInCents / 100m;

    public string? PaymentCollectionMethod { get; }

    public long CustomerId { get; }

    public string? CustomerReference { get; }

    public DateTimeOffset? CreatedAt { get; }

    public DateTimeOffset? ActivatedAt { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the provider will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? TrialEndedAt { get; }

    public DateTimeOffset? CanceledAt { get; }
}
