namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe operation. <see cref="AlreadyExisted"/> is <c>true</c> when the caller
/// was already enrolled in the plan and no new subscription was created (idempotent replay).
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }

    public bool AlreadyExisted { get; }
}
