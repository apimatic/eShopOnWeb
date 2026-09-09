namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> distinguishes a freshly created
/// enrollment from an idempotent no-op (the user was already subscribed to the same plan).
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
