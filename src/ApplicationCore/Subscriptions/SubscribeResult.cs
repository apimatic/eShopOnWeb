namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt. <see cref="Created"/> distinguishes a brand new enrollment from
/// an idempotent replay (double-click, client retry, or an already-subscribed shopper), which lets
/// the API answer <c>201 Created</c> versus <c>200 OK</c> honestly.
/// </summary>
public class SubscribeResult
{
    private SubscribeResult(CustomerSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public CustomerSubscription Subscription { get; }

    public bool Created { get; }

    public static SubscribeResult NewlyCreated(CustomerSubscription subscription) => new(subscription, true);

    public static SubscribeResult AlreadySubscribed(CustomerSubscription subscription) => new(subscription, false);
}
