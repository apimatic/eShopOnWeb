namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public CustomerSubscription Subscription { get; }
    public bool Created { get; }
}
