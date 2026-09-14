namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. The operation is idempotent: if the customer already had a
/// live subscription to the requested plan, that existing subscription is returned with
/// <see cref="WasCreated"/> set to <c>false</c> rather than creating a duplicate.
/// </summary>
public sealed class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, bool wasCreated, int maxioCustomerId)
    {
        Subscription = subscription;
        WasCreated = wasCreated;
        MaxioCustomerId = maxioCustomerId;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary><c>true</c> when a new subscription was created; <c>false</c> when an existing one was returned.</summary>
    public bool WasCreated { get; }

    /// <summary>The Maxio customer id that owns the subscription.</summary>
    public int MaxioCustomerId { get; }
}
