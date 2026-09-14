namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadySubscribed"/> is true when an
/// existing live subscription to the requested plan was returned instead of creating
/// a new one (idempotent behaviour that makes a double-click safe).
/// </summary>
public sealed class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public CustomerSubscription Subscription { get; }
    public bool AlreadySubscribed { get; }
}
