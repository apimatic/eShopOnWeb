namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> distinguishes a freshly
/// created subscription from an idempotent hit where the shopper was already enrolled in the
/// plan, so callers can surface the difference (201 vs 200) without a second round-trip.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>True when the shopper already had an active subscription to this plan.</summary>
    public bool AlreadyExisted { get; }
}
