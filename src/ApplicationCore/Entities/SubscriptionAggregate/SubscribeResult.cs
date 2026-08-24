namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Outcome of a subscribe call. <see cref="AlreadySubscribed"/> distinguishes an idempotent
/// replay (the pre-existing live subscription) from a freshly created enrollment.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public CustomerSubscription Subscription { get; }
    public bool AlreadySubscribed { get; }
}
