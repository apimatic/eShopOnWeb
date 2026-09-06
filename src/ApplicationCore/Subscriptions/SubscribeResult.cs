namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is what makes the operation observably
/// idempotent: a repeated request returns the subscription that the first request created.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }
    public bool AlreadyExisted { get; }
}
