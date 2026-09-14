namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> distinguishes a freshly
/// created subscription from one that was already present (idempotent replay of a double-click).
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>True when an equivalent live subscription already existed and was returned as-is.</summary>
    public bool AlreadyExisted { get; }
}
