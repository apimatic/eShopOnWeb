namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>
    /// True when this call enrolled the shopper. False when an equivalent subscription already
    /// existed and was returned instead (for example on a double-clicked subscribe button).
    /// </summary>
    public bool Created { get; }
}
