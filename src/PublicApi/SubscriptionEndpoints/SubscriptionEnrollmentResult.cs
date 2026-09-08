namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Outcome of an idempotent subscribe attempt: <see cref="Created"/> is true when this call enrolled
/// the shopper in a new subscription, false when the shopper was already subscribed and the existing
/// subscription is returned unchanged.
/// </summary>
public class SubscriptionEnrollmentResult
{
    public SubscriptionEnrollmentResult(bool created, SubscriptionDto subscription)
    {
        Created = created;
        Subscription = subscription;
    }

    public bool Created { get; }

    public SubscriptionDto Subscription { get; }
}
