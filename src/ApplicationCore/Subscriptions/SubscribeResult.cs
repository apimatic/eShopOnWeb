namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscribeResult
{
    public SubscribeResult(SubscriptionEnrollment subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public SubscriptionEnrollment Subscription { get; }
    public bool AlreadySubscribed { get; }
}
