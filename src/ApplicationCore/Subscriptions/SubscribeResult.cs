namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    /// <summary>The active (or newly created) subscription.</summary>
    public CustomerSubscription Subscription { get; }

    /// <summary>
    /// True when the shopper was already subscribed to this plan and the existing subscription was
    /// returned instead of creating a duplicate (idempotent hit, e.g. a double-click).
    /// </summary>
    public bool AlreadyExisted { get; }
}
