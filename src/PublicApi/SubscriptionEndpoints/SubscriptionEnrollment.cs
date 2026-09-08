namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionEnrollment
{
    public SubscriptionEnrollment(SubscriptionDto subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public SubscriptionDto Subscription { get; }

    public bool Created { get; }
}
