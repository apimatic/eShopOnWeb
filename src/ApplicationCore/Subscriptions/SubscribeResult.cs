namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadySubscribed"/> distinguishes a fresh
/// enrollment from an idempotent replay (a double-click, or a retry after a lost response).
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, SubscriptionPlan plan, bool alreadySubscribed)
    {
        Subscription = subscription;
        Plan = plan;
        AlreadySubscribed = alreadySubscribed;
    }

    public CustomerSubscription Subscription { get; }

    public SubscriptionPlan Plan { get; }

    /// <summary>True when the shopper already had a live subscription and nothing new was created.</summary>
    public bool AlreadySubscribed { get; }
}
