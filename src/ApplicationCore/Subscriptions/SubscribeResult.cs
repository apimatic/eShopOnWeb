namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a <see cref="ISubscriptionService.SubscribeAsync"/> call.
/// </summary>
public class SubscribeResult
{
    /// <summary>The active subscription for the shopper on the requested plan.</summary>
    public ShopperSubscription Subscription { get; init; } = default!;

    /// <summary>The Maxio customer id for the shopper.</summary>
    public int CustomerId { get; init; }

    /// <summary>
    /// True when an equivalent active subscription already existed and was returned instead of creating a
    /// duplicate (e.g. a double-clicked subscribe). False when a new subscription was created by this call.
    /// </summary>
    public bool AlreadyExisted { get; init; }

    /// <summary>True when a new Maxio customer had to be created for the shopper during this call.</summary>
    public bool CustomerCreated { get; init; }
}
