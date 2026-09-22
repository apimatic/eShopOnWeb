namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The local lifecycle of a <see cref="BuyerSubscription"/> claim row — deliberately distinct from the
/// provider's own subscription state. <see cref="Unknown"/> means the provider write was sent but its
/// outcome could not be confirmed; it is NOT a failure, and a reconciliation re-read settles it.
/// </summary>
public enum BillingRecordStatus
{
    Pending = 0,
    Provisioned = 1,
    Failed = 2,
    Unknown = 3
}
