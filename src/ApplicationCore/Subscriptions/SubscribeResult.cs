namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of <see cref="Interfaces.ISubscriptionService.SubscribeAsync"/>.
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
    /// False when an equivalent subscription already existed and was returned instead of being
    /// created again (double-click, retry, or a caller replaying an idempotency key).
    /// </summary>
    public bool Created { get; }
}
