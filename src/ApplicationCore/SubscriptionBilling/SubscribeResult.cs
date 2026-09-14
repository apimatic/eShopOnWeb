namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Result of an idempotent subscribe operation.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(SubscriptionDetailsDto subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public SubscriptionDetailsDto Subscription { get; }

    /// <summary>
    /// True when the subscription was created by this call; false when an equivalent
    /// subscription already existed and was returned instead (idempotent replay).
    /// </summary>
    public bool Created { get; }
}
