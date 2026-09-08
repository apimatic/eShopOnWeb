namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public CustomerSubscription Subscription { get; }
    public bool Created { get; }
}
