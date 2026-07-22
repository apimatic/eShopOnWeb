namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The lifecycle transitions offered on the management surface (UC4).
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume,
    Cancel,
    Reactivate
}
