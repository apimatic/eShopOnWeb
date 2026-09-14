using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, SubscriptionPlan plan, bool alreadySubscribed)
    {
        Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        AlreadySubscribed = alreadySubscribed;
    }

    public CustomerSubscription Subscription { get; }

    public SubscriptionPlan Plan { get; }

    /// <summary>
    /// True when the shopper already had a live subscription to the plan, so the existing one was
    /// returned instead of a second one being created (double-click, client retry, ...).
    /// </summary>
    public bool AlreadySubscribed { get; }
}
