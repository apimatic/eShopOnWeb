namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of a subscribe request - distinguishes a freshly created enrollment from an
/// already-active subscription that was returned instead of double-enrolling the customer.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(BillingSubscription subscription, bool wasAlreadyEnrolled)
    {
        Subscription = subscription;
        WasAlreadyEnrolled = wasAlreadyEnrolled;
    }

    public BillingSubscription Subscription { get; }
    public bool WasAlreadyEnrolled { get; }
}
