namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The lifecycle transitions a subscription owner or an administrator can request (UC4).
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume,
    Cancel,
    Reactivate
}
