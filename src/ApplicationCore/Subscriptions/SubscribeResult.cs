namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> distinguishes a freshly
/// created subscription from an idempotent replay (e.g. a double-clicked Subscribe button),
/// where the shopper's existing enrollment for the plan is returned unchanged.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>
    /// <c>true</c> when the shopper was already enrolled in the plan and no new subscription
    /// was created; <c>false</c> when a new subscription was created by this request.
    /// </summary>
    public bool AlreadyExisted { get; }
}
