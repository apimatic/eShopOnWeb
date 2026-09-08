namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is true when an existing
/// subscription for this (user, plan) was returned instead of creating a new one — the
/// idempotency signal that makes a double-click safe.
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
