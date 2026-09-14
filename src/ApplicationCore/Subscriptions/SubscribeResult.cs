namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe operation. When <see cref="AlreadySubscribed"/> is true the
/// caller already had a live subscription to the requested plan and no new subscription was
/// created (idempotent behaviour), otherwise a new subscription was enrolled.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadySubscribed, int customerId)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
        CustomerId = customerId;
    }

    public CustomerSubscription Subscription { get; }
    public bool AlreadySubscribed { get; }
    public int CustomerId { get; }
}
