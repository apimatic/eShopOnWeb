namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. Because the flow is idempotent, a request may either create a new
/// subscription or return the shopper's pre-existing one; <see cref="AlreadyExisted"/> distinguishes the two.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>True when the shopper was already subscribed to this plan and no new subscription was created.</summary>
    public bool AlreadyExisted { get; }
}
