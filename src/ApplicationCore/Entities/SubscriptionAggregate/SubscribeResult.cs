namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of a subscribe attempt: the subscription itself plus whether this call is what
/// created it, so the API can answer 201 for a new enrollment and 200 for a repeat.
/// </summary>
public class SubscribeResult
{
    private SubscribeResult(CustomerSubscription subscription, bool created, bool customerCreated)
    {
        Subscription = subscription;
        Created = created;
        CustomerCreated = customerCreated;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>False when the shopper was already enrolled and this call changed nothing.</summary>
    public bool Created { get; }

    /// <summary>True when this call also created the shopper's billing customer record.</summary>
    public bool CustomerCreated { get; }

    public static SubscribeResult NewlySubscribed(CustomerSubscription subscription, bool customerCreated) =>
        new(subscription, created: true, customerCreated);

    public static SubscribeResult AlreadySubscribed(CustomerSubscription subscription) =>
        new(subscription, created: false, customerCreated: false);
}
