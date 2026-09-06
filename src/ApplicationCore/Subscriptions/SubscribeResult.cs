namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(Subscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public Subscription Subscription { get; }

    /// <summary>
    /// True when the request resolved to a subscription that already existed, so nothing new was
    /// billed. Lets callers distinguish a fresh enrollment from an idempotent replay.
    /// </summary>
    public bool AlreadySubscribed { get; }
}
