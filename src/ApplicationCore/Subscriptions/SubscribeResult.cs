namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Outcome of a subscribe attempt.</summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>
    /// False when the request was a replay - the shopper was already enrolled and the existing
    /// subscription is being returned unchanged.
    /// </summary>
    public bool Created { get; }
}
