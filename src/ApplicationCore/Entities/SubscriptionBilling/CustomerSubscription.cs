using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// A shopper's enrollment in a plan, projected from a billing-system subscription.
/// </summary>
public sealed class CustomerSubscription
{
    public CustomerSubscription(
        long id,
        string state,
        string planHandle,
        string planName,
        long priceInCents,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? createdAt,
        string customerReference)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        CreatedAt = createdAt;
        CustomerReference = customerReference;
    }

    /// <summary>Billing-system subscription id.</summary>
    public long Id { get; }

    /// <summary>Lifecycle state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public long PriceInCents { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the subscription is next assessed/billed.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? CreatedAt { get; }

    public string CustomerReference { get; }

    /// <summary>
    /// States that represent a live enrollment. An existing subscription in one of these states to the
    /// same plan blocks creation of a duplicate (idempotent subscribe / double-click protection).
    /// Terminal states (<c>canceled</c>, <c>expired</c>, <c>failed_to_create</c>) do not block re-subscribing.
    /// </summary>
    public bool IsLive => State is not ("canceled" or "expired" or "failed_to_create");
}
