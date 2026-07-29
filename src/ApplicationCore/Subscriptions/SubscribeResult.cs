namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is true when an equivalent live
/// subscription was already present, so no new subscription was created (idempotent re-entry).
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(SubscriberSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public SubscriberSubscription Subscription { get; }

    public bool AlreadyExisted { get; }
}
