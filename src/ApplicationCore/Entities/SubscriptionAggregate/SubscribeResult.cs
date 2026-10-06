namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of a subscribe request. <see cref="Created"/> is false when the shopper
/// already had an active subscription to the requested plan (idempotent re-subscribe).
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(SubscriptionInfo subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public SubscriptionInfo Subscription { get; }
    public bool Created { get; }
}
