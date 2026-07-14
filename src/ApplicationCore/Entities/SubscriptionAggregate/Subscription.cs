using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The eShopOnWeb-side view of a Maxio subscription. Never persisted locally — the eShop user
/// (<see cref="BuyerId"/>) is idempotently resolved to a Maxio customer reference on every call
/// (§8: stateless mapping), so this is always a fresh projection of the provider's state.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(
        int subscriptionId,
        string buyerId,
        string customerReference,
        string productHandle,
        string productName,
        long priceInCents,
        SubscriptionStatus status,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? delayedCancelAt,
        long? balanceInCents)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        Id = subscriptionId;
        BuyerId = buyerId;
        CustomerReference = customerReference;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        Status = status;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        DelayedCancelAt = delayedCancelAt;
        BalanceInCents = balanceInCents;
    }

    public string BuyerId { get; }
    public string CustomerReference { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public long PriceInCents { get; }
    public SubscriptionStatus Status { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    public bool CancelAtEndOfPeriod { get; }
    public DateTimeOffset? DelayedCancelAt { get; }
    public long? BalanceInCents { get; }
}
