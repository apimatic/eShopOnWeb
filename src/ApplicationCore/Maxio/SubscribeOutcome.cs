namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class SubscribeOutcome
{
    public SubscribeOutcome(MaxioSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public MaxioSubscription Subscription { get; }

    public bool AlreadySubscribed { get; }
}
