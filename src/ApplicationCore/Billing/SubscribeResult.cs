namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Outcome of a subscribe attempt. <see cref="AlreadySubscribed"/> distinguishes a fresh enrollment
/// from an idempotent replay (a double-click, or a retry of a request that had already succeeded).
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public CustomerSubscription Subscription { get; }

    public bool AlreadySubscribed { get; }
}
