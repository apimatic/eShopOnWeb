namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe operation. <see cref="AlreadySubscribed"/> lets the
/// caller distinguish a freshly-created subscription from an idempotent no-op
/// (e.g. a double-click that returned the existing subscription).
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    /// <summary>The active subscription (either newly created or pre-existing).</summary>
    public CustomerSubscription Subscription { get; }

    /// <summary>
    /// True when the user already had a live subscription to this plan and no new
    /// subscription was created.
    /// </summary>
    public bool AlreadySubscribed { get; }
}
