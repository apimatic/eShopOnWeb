using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, projected from a Maxio
/// subscription. Confirms plan, price, lifecycle state and the next billing date back to
/// the user.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        int subscriptionId,
        string planHandle,
        string planName,
        long priceInCents,
        string currency,
        string state,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt,
        string customerReference)
    {
        SubscriptionId = subscriptionId;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        CustomerReference = customerReference;
    }

    /// <summary>Maxio subscription id.</summary>
    public int SubscriptionId { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public long PriceInCents { get; }

    public string Currency { get; }

    /// <summary>Maxio subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the subscription bills next.</summary>
    public DateTimeOffset? NextBillingAt { get; }

    /// <summary>The Maxio customer reference this subscription belongs to.</summary>
    public string CustomerReference { get; }

    public decimal Price => decimal.Divide(PriceInCents, 100m);
}
