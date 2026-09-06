namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadySubscribed"/> is what makes the operation
/// idempotent: a repeated request returns the enrollment that already exists rather than
/// creating a second one.
/// </summary>
public class SubscribeResult
{
    private SubscribeResult(SubscriberSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public SubscriberSubscription Subscription { get; }

    /// <summary>True when this request created the enrollment; false when it already existed.</summary>
    public bool Created { get; }

    public static SubscribeResult NewlyCreated(SubscriberSubscription subscription) => new(subscription, true);

    public static SubscribeResult AlreadySubscribed(SubscriberSubscription subscription) => new(subscription, false);
}
