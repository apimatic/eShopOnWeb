using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription a customer holds, as reported by Maxio. This is the confirmation returned to the
/// shopper after subscribing and the shape listed under "my subscriptions".
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        int id,
        int customerId,
        string? customerReference,
        string state,
        string? planHandle,
        string? planName,
        long currentPriceInCents,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        DateTimeOffset? createdAt)
    {
        Id = id;
        CustomerId = customerId;
        CustomerReference = customerReference;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        CurrentPriceInCents = currentPriceInCents;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        CreatedAt = createdAt;
    }

    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; }

    public int CustomerId { get; }

    public string? CustomerReference { get; }

    /// <summary>The Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; }

    /// <summary>The API handle of the subscribed plan (product).</summary>
    public string? PlanHandle { get; }

    public string? PlanName { get; }

    /// <summary>The recurring price in integer cents.</summary>
    public long CurrentPriceInCents { get; }

    /// <summary>End of the current billing period (when the next charge is scheduled).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When Maxio will next attempt to bill the subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? CreatedAt { get; }

    /// <summary>
    /// States in which a subscription is not considered live, so a new subscription to the same plan
    /// may be created rather than treated as a duplicate.
    /// </summary>
    public bool IsLive =>
        !string.Equals(State, "canceled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "expired", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "failed_to_create", StringComparison.OrdinalIgnoreCase);
}
