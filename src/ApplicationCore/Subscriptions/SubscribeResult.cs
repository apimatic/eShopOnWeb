namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="Created"/> distinguishes a brand new enrollment
/// from an idempotent replay that returned the shopper's existing subscription.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>True when this call enrolled the shopper; false when an existing subscription was returned.</summary>
    public bool Created { get; }
}
