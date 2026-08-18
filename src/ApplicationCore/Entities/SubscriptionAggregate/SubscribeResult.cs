namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscribeResult
{
    public SubscribeResult(ShopperSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public ShopperSubscription Subscription { get; }
    public bool Created { get; }
}
