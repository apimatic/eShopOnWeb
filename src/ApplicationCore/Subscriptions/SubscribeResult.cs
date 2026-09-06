using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Outcome of a subscribe attempt.</summary>
public sealed class SubscribeResult
{
    private SubscribeResult(CustomerSubscription subscription, SubscriptionPlan plan, bool created)
    {
        Subscription = Guard.Against.Null(subscription, nameof(subscription));
        Plan = Guard.Against.Null(plan, nameof(plan));
        Created = created;
    }

    public CustomerSubscription Subscription { get; }

    public SubscriptionPlan Plan { get; }

    /// <summary>
    /// False when the shopper was already enrolled and the existing subscription was returned
    /// instead of a second one being created.
    /// </summary>
    public bool Created { get; }

    public static SubscribeResult NewlyCreated(CustomerSubscription subscription, SubscriptionPlan plan) =>
        new(subscription, plan, created: true);

    public static SubscribeResult AlreadyExisted(CustomerSubscription subscription, SubscriptionPlan plan) =>
        new(subscription, plan, created: false);
}
