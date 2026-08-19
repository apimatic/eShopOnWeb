namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

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
