namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscribeResult
{
    public SubscribeResult(BillingSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    public BillingSubscription Subscription { get; }

    /// <summary>
    /// False when an existing live subscription was returned (idempotent replay).
    /// </summary>
    public bool Created { get; }
}
