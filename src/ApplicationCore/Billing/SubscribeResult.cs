namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Outcome of a subscribe attempt. <see cref="AlreadySubscribed"/> distinguishes a fresh
/// enrollment from an idempotent replay (a double-click, or a client retry).
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>
    /// True when the shopper already held a live subscription to this plan and nothing new was
    /// created; the subscription returned is the pre-existing one.
    /// </summary>
    public bool AlreadySubscribed { get; }

    public static SubscribeResult Created(CustomerSubscription subscription) => new(subscription, false);

    public static SubscribeResult Existing(CustomerSubscription subscription) => new(subscription, true);
}
