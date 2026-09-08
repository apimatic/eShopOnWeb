namespace Microsoft.eShopWeb.Maxio.Contracts;

/// <summary>
/// Outcome of a subscribe call. When <see cref="Created"/> is true the subscription was
/// just created; when false an existing, still-live subscription to the same plan is returned
/// (idempotent replay).
/// </summary>
public sealed class SubscribeResult
{
    public SubscribeResult(SubscriptionRecord subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public SubscriptionRecord Subscription { get; }

    public bool Created { get; }
}
