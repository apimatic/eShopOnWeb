namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The four lifecycle transitions a customer or administrator can request (UC4).
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume,
    CancelImmediately,
    CancelAtPeriodEnd,
    Reactivate
}
