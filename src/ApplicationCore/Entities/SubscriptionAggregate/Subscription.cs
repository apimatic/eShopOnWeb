using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Ties an eShopOnWeb user identity to its Maxio customer/subscription references.
/// Built fresh from the billing provider's response on every call (see §8 of plan.md:
/// the userId ↔ subscription mapping is stateless, idempotent on the user reference,
/// rather than persisted via EfRepository).
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Subscription() { }

    public Subscription(
        string userId,
        int billingSubscriptionId,
        int billingCustomerId,
        string? billingCustomerReference,
        string productHandle,
        string? productName,
        long priceInCents,
        BillingSubscriptionState state,
        DateTimeOffset? nextBillingDate,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? delayedCancelAt)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        Id = billingSubscriptionId;
        UserId = userId;
        BillingCustomerId = billingCustomerId;
        BillingCustomerReference = billingCustomerReference;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        State = state;
        NextBillingDate = nextBillingDate;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        DelayedCancelAt = delayedCancelAt;
    }

    public string UserId { get; private set; }
    public int BillingCustomerId { get; private set; }
    public string? BillingCustomerReference { get; private set; }
    public string ProductHandle { get; private set; }
    public string? ProductName { get; private set; }
    public long PriceInCents { get; private set; }
    public BillingSubscriptionState State { get; private set; }
    public DateTimeOffset? NextBillingDate { get; private set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }
    public DateTimeOffset? DelayedCancelAt { get; private set; }
}
