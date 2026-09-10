namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe operation.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    /// <summary>The active subscription for the shopper on the requested plan.</summary>
    public CustomerSubscription Subscription { get; }

    /// <summary>
    /// True when a matching live subscription already existed and was returned unchanged
    /// (idempotent replay of a prior subscribe / double-click); false when it was created now.
    /// </summary>
    public bool AlreadySubscribed { get; }
}
