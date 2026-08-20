namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public sealed class SubscribeResult
{
    public SubscribeResult(BillingSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public BillingSubscription Subscription { get; }
    public bool Created { get; }
}
