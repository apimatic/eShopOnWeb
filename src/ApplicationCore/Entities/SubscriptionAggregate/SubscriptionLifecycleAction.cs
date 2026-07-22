namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The lifecycle transitions offered by the subscription management surface (UC4).
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause,
    Resume,
    Cancel,
    Reactivate
}
