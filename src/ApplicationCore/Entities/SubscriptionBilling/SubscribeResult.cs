namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is <c>true</c> when an active
/// subscription to the requested plan was already present (idempotent replay — e.g. a double-click),
/// in which case no new Maxio subscription was created.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(SubscriptionInfo subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public SubscriptionInfo Subscription { get; }

    public bool AlreadyExisted { get; }
}
