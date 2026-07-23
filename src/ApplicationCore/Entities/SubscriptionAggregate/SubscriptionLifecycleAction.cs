namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The four lifecycle transitions exposed by the subscription management surface (UC4).
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume,
    Cancel,
    Reactivate
}
