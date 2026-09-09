namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// Outcome of a subscribe operation, carrying the resulting subscription and whether it already existed
/// (so callers can convey idempotent semantics, e.g. 200 vs 201).
/// </summary>
public sealed class SubscriptionResult
{
    public SubscriptionResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>True when the shopper was already subscribed to this plan and no new subscription was created.</summary>
    public bool AlreadyExisted { get; }
}
