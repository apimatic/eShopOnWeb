namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscribeToPlanResult
{
    public SubscribeToPlanResult(ShopperSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public ShopperSubscription Subscription { get; }
    public bool Created { get; }
}
