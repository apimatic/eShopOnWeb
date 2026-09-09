namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingSubscription;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> distinguishes a freshly created
/// subscription from an idempotent hit (the shopper was already enrolled in the plan), so callers
/// never create a duplicate on a double-click.
/// </summary>
public class SubscriptionEnrollmentResult
{
    public SubscriptionEnrollmentResult(CustomerSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }

    public bool AlreadyExisted { get; }
}
