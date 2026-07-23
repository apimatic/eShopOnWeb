using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription exactly as the billing provider reports it. The provider is the system of
/// record (§1.1), so this type is a read-model: eShopOnWeb never mutates it locally.
/// </summary>
public sealed class BillingSubscription
{
    public BillingSubscription(long id,
        SubscriptionState state,
        long customerId,
        string? customerReference,
        string productHandle,
        string productName,
        int productPriceInCents,
        DateTimeOffset? currentPeriodStartsAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? delayedCancelAt,
        string? nextProductHandle,
        int balanceInCents,
        string currency)
    {
        Id = Guard.Against.NegativeOrZero(id, nameof(id));
        State = state;
        CustomerId = customerId;
        CustomerReference = customerReference;
        ProductHandle = productHandle;
        ProductName = productName;
        ProductPriceInCents = productPriceInCents;
        CurrentPeriodStartsAt = currentPeriodStartsAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        DelayedCancelAt = delayedCancelAt;
        NextProductHandle = nextProductHandle;
        BalanceInCents = balanceInCents;
        Currency = currency;
    }

    public long Id { get; }

    public SubscriptionState State { get; }

    public long CustomerId { get; }

    public string? CustomerReference { get; }

    public string ProductHandle { get; }

    public string ProductName { get; }

    /// <summary>Recurring price of the current plan in minor units (cents).</summary>
    public int ProductPriceInCents { get; }

    public DateTimeOffset? CurrentPeriodStartsAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the provider will next assess (bill) this subscription — the "next billing date" UC1 shows.</summary>
    public DateTimeOffset? NextAssessmentAt { get; }

    public bool CancelAtEndOfPeriod { get; }

    /// <summary>Effective date of a pending end-of-period cancellation, when one is scheduled.</summary>
    public DateTimeOffset? DelayedCancelAt { get; }

    /// <summary>Set when a plan change has been scheduled for the next renewal (UC3).</summary>
    public string? NextProductHandle { get; }

    public int BalanceInCents { get; }

    public string Currency { get; }

    /// <summary>Recurring price of the current plan in major units (e.g. dollars).</summary>
    public decimal ProductPrice => ProductPriceInCents / 100m;

    /// <summary>Outstanding balance in major units.</summary>
    public decimal Balance => BalanceInCents / 100m;

    /// <summary>
    /// True when the subscription may accrue usage and be managed. Cancelled/expired subscriptions
    /// are excluded, which is what UC1's duplicate-subscribe check and UC2's precondition rely on.
    /// </summary>
    public bool IsActive => State is SubscriptionState.Active or SubscriptionState.Trialing;
}
