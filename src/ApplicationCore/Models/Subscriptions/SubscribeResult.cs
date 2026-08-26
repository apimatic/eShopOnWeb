namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>
    /// True when the customer already held a live subscription to the plan, so the
    /// existing subscription was returned instead of creating a duplicate.
    /// </summary>
    public bool AlreadyExisted { get; }
}
