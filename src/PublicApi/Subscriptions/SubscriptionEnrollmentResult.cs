using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Outcome of an idempotent subscribe operation.
/// </summary>
public class SubscriptionEnrollmentResult
{
    public SubscriptionEnrollmentResult(MaxioSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    /// <summary>The subscription that is now in effect for the shopper.</summary>
    public MaxioSubscription Subscription { get; }

    /// <summary>
    /// True when the subscription was created by this call; false when the shopper was already
    /// subscribed to the plan and the existing subscription is being returned.
    /// </summary>
    public bool Created { get; }
}
