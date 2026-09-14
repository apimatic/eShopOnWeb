namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyEnrolled"/> is true when the shopper
/// already had a live subscription to the requested plan, in which case the existing
/// subscription is returned instead of a new one being created (idempotency).
/// </summary>
public class SubscriptionResult
{
    public SubscriptionResult(CustomerSubscription subscription, bool alreadyEnrolled)
    {
        Subscription = subscription;
        AlreadyEnrolled = alreadyEnrolled;
    }

    public CustomerSubscription Subscription { get; }

    public bool AlreadyEnrolled { get; }
}
