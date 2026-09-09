namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Outcome of a subscribe operation. <see cref="Created"/> is false when the
/// user was already subscribed and the existing subscription was returned.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(UserSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public UserSubscription Subscription { get; }
    public bool Created { get; }
}
