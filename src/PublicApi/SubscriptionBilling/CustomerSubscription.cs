using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// A Maxio subscription belonging to an eShopOnWeb shopper, with the fields the eShopOnWeb
/// experience surfaces (plan, price, state and next billing date).
/// </summary>
public sealed class CustomerSubscription
{
    public CustomerSubscription(
        long subscriptionId,
        string state,
        string? reference,
        string planHandle,
        string? planName,
        long priceInCents,
        int? interval,
        string? intervalUnit,
        DateTimeOffset? periodStartedAt,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? activatedAt,
        DateTimeOffset? createdAt,
        DateTimeOffset? updatedAt,
        string? currency,
        string? paymentCollectionMethod)
    {
        SubscriptionId = subscriptionId;
        State = state;
        Reference = reference;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        PeriodStartedAt = periodStartedAt;
        NextBillingAt = nextBillingAt;
        ActivatedAt = activatedAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Currency = currency;
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    public long SubscriptionId { get; }

    public string State { get; }

    public string? Reference { get; }

    public string PlanHandle { get; }

    public string? PlanName { get; }

    public long PriceInCents { get; }

    public int? Interval { get; }

    public string? IntervalUnit { get; }

    public DateTimeOffset? PeriodStartedAt { get; }

    /// <summary>Next billing/assessment date for an active subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? ActivatedAt { get; }

    public DateTimeOffset? CreatedAt { get; }

    public DateTimeOffset? UpdatedAt { get; }

    public string? Currency { get; }

    public string? PaymentCollectionMethod { get; }

    /// <summary>
    /// Whether the subscription represents an ongoing (not cancelled/expired) entitlement.
    /// Used to keep "subscribe" idempotent without blocking a re-subscribe after cancellation.
    /// </summary>
    public bool IsOngoing =>
        !string.Equals(State, "canceled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "expired", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "cancel_pending", StringComparison.OrdinalIgnoreCase);
}
