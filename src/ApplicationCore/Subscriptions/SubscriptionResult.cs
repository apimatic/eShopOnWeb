namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe request: the resulting subscription, plus whether it was created by
/// this call or already existed (so a double-click / retry is reported honestly rather than as new).
/// </summary>
public class SubscriptionResult
{
    public SubscriptionResult(SubscriptionDetails subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public SubscriptionDetails Subscription { get; }

    /// <summary>True when an existing subscription was returned instead of creating a new one.</summary>
    public bool AlreadyExisted { get; }
}
