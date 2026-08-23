namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public enum SubscriptionOperationStatus
{
    Pending,
    NeedsReconciliation,
    Confirmed,
    Rejected
}
