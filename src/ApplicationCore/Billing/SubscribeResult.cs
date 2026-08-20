namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscribeResult
{
    public SubscribeResult(ShopperSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public ShopperSubscription Subscription { get; }
    public bool Created { get; }
}
