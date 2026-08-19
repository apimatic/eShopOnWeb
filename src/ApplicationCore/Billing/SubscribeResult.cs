namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscribeResult
{
    public SubscribeResult(UserSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public UserSubscription Subscription { get; }
    public bool Created { get; }
}
