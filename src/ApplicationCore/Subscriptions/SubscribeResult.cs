using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
public class SubscribeResult
{
    private SubscribeResult(CustomerSubscription subscription, bool created)
    {
        Subscription = Guard.Against.Null(subscription, nameof(subscription));
        Created = created;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>
    /// True when this call created the subscription; false when an equivalent subscription already
    /// existed and was returned unchanged.
    /// </summary>
    public bool Created { get; }

    public static SubscribeResult NewlyCreated(CustomerSubscription subscription) => new(subscription, true);

    public static SubscribeResult AlreadyExisted(CustomerSubscription subscription) => new(subscription, false);
}
