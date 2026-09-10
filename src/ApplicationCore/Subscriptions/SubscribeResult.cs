namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> distinguishes a brand-new
/// enrollment from an idempotent no-op (the shopper already had a live subscription to the
/// plan, e.g. from a double-click), so callers can report the correct HTTP status.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(SubscriptionSummary subscription, bool alreadyExisted, int customerId)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
        CustomerId = customerId;
    }

    public SubscriptionSummary Subscription { get; }

    /// <summary>True when a matching live subscription already existed and none was created.</summary>
    public bool AlreadyExisted { get; }

    /// <summary>The Maxio customer id the subscription belongs to.</summary>
    public int CustomerId { get; }
}
