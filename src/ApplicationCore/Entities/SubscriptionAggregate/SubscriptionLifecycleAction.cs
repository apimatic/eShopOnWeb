namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The four UC4 lifecycle transitions a customer or admin can request.
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause,
    Resume,
    CancelImmediate,
    CancelAtEndOfPeriod,
    Reactivate
}
