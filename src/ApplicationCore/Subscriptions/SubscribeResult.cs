namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is <c>true</c> when an
/// eligible (live) subscription to the requested plan was already present, so the request
/// was idempotent and no new subscription was created.
/// </summary>
public sealed class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }

    public bool AlreadyExisted { get; }
}
