namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>Outcome of a subscribe attempt.</summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>
    /// True when the subscriber was already enrolled in the plan and the existing subscription was
    /// returned instead of a new one being created.
    /// </summary>
    public bool AlreadySubscribed { get; }
}
