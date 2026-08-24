namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class SubscribeResult
{
    public SubscribeResult(SubscriptionDto subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public SubscriptionDto Subscription { get; }

    /// <summary>True when a new Maxio subscription was created; false when an existing live one was returned.</summary>
    public bool Created { get; }
}
