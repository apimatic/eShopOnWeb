namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
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
    /// True when the caller already held a live subscription on the requested plan and nothing new was
    /// created. The endpoint answers <c>200 OK</c> rather than <c>201 Created</c> in that case, so a
    /// double-click is observably idempotent.
    /// </summary>
    public bool AlreadySubscribed { get; }
}
