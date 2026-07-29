namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadySubscribed"/> is true when an existing live
/// subscription to the same plan was found and returned instead of creating a new one, which is how
/// the flow stays idempotent against double-clicks / retries.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public CustomerSubscription Subscription { get; }

    public bool AlreadySubscribed { get; }
}
