namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt. <see cref="AlreadyExisted"/> distinguishes a freshly
/// created subscription from an idempotent hit (the customer already had a live
/// subscription to the same plan — e.g. a double-click), so callers can respond 201 vs 200.
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
