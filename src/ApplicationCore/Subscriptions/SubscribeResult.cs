namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe operation, including whether an existing enrolment was reused
/// (so a double-click does not create a second subscription).
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, long customerId, bool alreadyExisted)
    {
        Subscription = subscription;
        CustomerId = customerId;
        AlreadyExisted = alreadyExisted;
    }

    public CustomerSubscription Subscription { get; }

    /// <summary>Billing-system customer id the subscription belongs to.</summary>
    public long CustomerId { get; }

    /// <summary>
    /// True when the caller was already subscribed to this plan and the existing
    /// subscription was returned instead of creating a new one.
    /// </summary>
    public bool AlreadyExisted { get; }
}
