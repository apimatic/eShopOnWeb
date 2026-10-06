namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionEnrollmentStatus
{
    /// <summary>
    /// The shopper's claim is held and the subscription is being created (or its outcome is not yet known).
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The billing system holds a subscription for this shopper.
    /// </summary>
    Completed = 1
}
