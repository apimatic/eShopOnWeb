namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe request.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(BillingSubscription subscription, SubscriptionPlan plan, bool alreadySubscribed)
    {
        Subscription = subscription;
        Plan = plan;
        AlreadySubscribed = alreadySubscribed;
    }

    public BillingSubscription Subscription { get; }

    public SubscriptionPlan Plan { get; }

    /// <summary>
    /// True when the shopper already held a live subscription to this plan and the existing one was
    /// returned instead of a second being created.
    /// </summary>
    public bool AlreadySubscribed { get; }
}
