namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe operation, distinguishing a freshly-created subscription from
/// an idempotent hit on an already-existing active subscription for the same plan.
/// </summary>
public sealed class SubscribeResult
{
    public required CustomerSubscription Subscription { get; init; }

    /// <summary>
    /// True when this call created a new subscription; false when an existing active
    /// subscription to the same plan was returned (idempotent re-subscribe / double-click).
    /// </summary>
    public required bool Created { get; init; }
}
